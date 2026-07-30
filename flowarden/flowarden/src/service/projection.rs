//! Projection gRPC service implementation.

use std::sync::Arc;

use futures_core::Stream;
use tokio_stream::{StreamExt, wrappers::WatchStream};
use tonic::{Request, Response, Status};

use super::{
    convert::{
        connection_to_proto_with_process, inspect_row_matches_filter, normalize_projection_top_n,
        projection_response_from_runtime_snapshot, tcp_connection_matches_filter,
        tcp_connection_to_proto,
    },
    proto::projection::{
        GetInspectPageRequest, GetLatestOverviewRequest, GetTcpConnectionsPageRequest,
        InspectPageResponse, OverviewSnapshotResponse, StreamOverviewRequest,
        TcpConnectionsPageResponse, projection_service_server::ProjectionService,
    },
    state::ServiceState,
};

#[derive(Clone)]
pub(crate) struct ProjectionServiceImpl {
    state: ServiceState,
}

impl ProjectionServiceImpl {
    pub(crate) fn new(state: ServiceState) -> Self {
        Self { state }
    }
}

#[tonic::async_trait]
impl ProjectionService for ProjectionServiceImpl {
    type StreamOverviewStream = std::pin::Pin<
        Box<dyn Stream<Item = std::result::Result<OverviewSnapshotResponse, Status>> + Send>,
    >;

    async fn get_latest_overview(
        &self,
        request: Request<GetLatestOverviewRequest>,
    ) -> std::result::Result<Response<OverviewSnapshotResponse>, Status> {
        let top_n = normalize_projection_top_n(request.into_inner().top_n);
        let runtime_snapshot = self.state.overview_tx.borrow().clone();

        projection_response_from_runtime_snapshot(
            runtime_snapshot,
            &self.state.geo,
            Some(self.state.process_lookup.as_ref()),
            Some(self.state.rdns_lookup.as_ref()),
            Some(self.state.signals.as_ref()),
            Some(self.state.syslog.as_ref()),
            top_n,
        )
        .map(Response::new)
        .map_err(|err| Status::internal(err.to_string()))
    }

    async fn stream_overview(
        &self,
        request: Request<StreamOverviewRequest>,
    ) -> std::result::Result<Response<Self::StreamOverviewStream>, Status> {
        let top_n = normalize_projection_top_n(request.into_inner().top_n);
        let receiver = self.state.overview_tx.subscribe();
        let initial = receiver.borrow().clone();
        let initial_response = projection_response_from_runtime_snapshot(
            initial,
            &self.state.geo,
            Some(self.state.process_lookup.as_ref()),
            Some(self.state.rdns_lookup.as_ref()),
            Some(self.state.signals.as_ref()),
            Some(self.state.syslog.as_ref()),
            top_n,
        )
        .map_err(|err| Status::internal(err.to_string()))?;

        let stream = WatchStream::from_changes(receiver).map({
            let geo = self.state.geo.clone();
            let process_lookup = Arc::clone(&self.state.process_lookup);
            let rdns_lookup = Arc::clone(&self.state.rdns_lookup);
            let signals = Arc::clone(&self.state.signals);
            let syslog = Arc::clone(&self.state.syslog);
            move |snapshot| {
                projection_response_from_runtime_snapshot(
                    snapshot,
                    &geo,
                    Some(process_lookup.as_ref()),
                    Some(rdns_lookup.as_ref()),
                    Some(signals.as_ref()),
                    Some(syslog.as_ref()),
                    top_n,
                )
                .map_err(|err| Status::internal(err.to_string()))
            }
        });

        let output = tokio_stream::once(Ok(initial_response)).chain(stream);
        Ok(Response::new(Box::pin(output)))
    }

    async fn get_inspect_page(
        &self,
        request: Request<GetInspectPageRequest>,
    ) -> std::result::Result<Response<InspectPageResponse>, Status> {
        let filter = request.into_inner();
        let top_n = normalize_projection_top_n(filter.top_n);
        let runtime_snapshot = self.state.overview_tx.borrow().clone();

        let mut geo = self
            .state
            .geo
            .lock()
            .map_err(|_| Status::internal("Failed to lock geo resolver"))?;
        let rows = runtime_snapshot
            .top_connections
            .iter()
            .filter_map(|connection| {
                let process = self
                    .state
                    .process_lookup
                    .resolve(connection, &runtime_snapshot.local_ips);
                let remote_ip = if runtime_snapshot.local_ips.contains(&connection.key.source_ip) {
                    connection.key.destination_ip
                } else if runtime_snapshot
                    .local_ips
                    .contains(&connection.key.destination_ip)
                {
                    connection.key.source_ip
                } else {
                    connection.key.destination_ip
                };
                let remote_asn_label = geo.resolve_asn(remote_ip).display_label();
                let row = connection_to_proto_with_process(
                    connection,
                    process.as_ref(),
                    remote_asn_label,
                    &runtime_snapshot.local_ips,
                );
                inspect_row_matches_filter(&row, &filter).then_some(row)
            })
            .take(top_n)
            .collect();

        Ok(Response::new(InspectPageResponse {
            state: "ready".to_string(),
            rows,
        }))
    }

    async fn get_tcp_connections_page(
        &self,
        request: Request<GetTcpConnectionsPageRequest>,
    ) -> std::result::Result<Response<TcpConnectionsPageResponse>, Status> {
        let filter = request.into_inner();
        let top_n = normalize_projection_top_n(filter.top_n);
        let runtime_snapshot = self.state.overview_tx.borrow().clone();

        let rows = runtime_snapshot
            .tcp_connections
            .iter()
            .map(tcp_connection_to_proto)
            .filter(|row| tcp_connection_matches_filter(row, &filter))
            .take(top_n)
            .collect();

        Ok(Response::new(TcpConnectionsPageResponse {
            state: "ready".to_string(),
            rows,
        }))
    }
}
