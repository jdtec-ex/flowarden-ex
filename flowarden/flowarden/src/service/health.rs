//! Health gRPC service implementation.

use tonic::{Request, Response, Status};

use super::{
    proto::health::{
        GetHealthRequest, GetVersionRequest, HealthResponse, VersionResponse,
        health_service_server::HealthService,
    },
    state::ServiceState,
};

#[derive(Clone)]
pub(crate) struct HealthServiceImpl {
    state: ServiceState,
}

impl HealthServiceImpl {
    pub(crate) fn new(state: ServiceState) -> Self {
        Self { state }
    }
}

#[tonic::async_trait]
impl HealthService for HealthServiceImpl {
    async fn get_health(
        &self,
        _request: Request<GetHealthRequest>,
    ) -> std::result::Result<Response<HealthResponse>, Status> {
        Ok(Response::new(HealthResponse {
            status: "ok".to_string(),
            started_at_unix_seconds: self.state.started_at_unix_seconds,
        }))
    }

    async fn get_version(
        &self,
        _request: Request<GetVersionRequest>,
    ) -> std::result::Result<Response<VersionResponse>, Status> {
        Ok(Response::new(VersionResponse {
            service: "flowarden-core-service".to_string(),
            // Marker helps UI/dev verify the running core includes enrichment workers.
            version: format!("{}+enrichment", env!("CARGO_PKG_VERSION")),
        }))
    }
}
