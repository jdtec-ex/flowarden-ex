//! Device discovery gRPC service implementation.

use std::time::Duration;

use flowarden_core::device::{list_devices, preview_devices};
use tonic::{Request, Response, Status};

use super::proto::discovery::{
    DeviceAddressSummary, DevicePreview, DeviceSummary, ListDevicePreviewsRequest,
    ListDevicePreviewsResponse, ListDevicesRequest, ListDevicesResponse,
    discovery_service_server::DiscoveryService,
};

#[derive(Clone, Default)]
pub(crate) struct DiscoveryServiceImpl;
#[tonic::async_trait]
impl DiscoveryService for DiscoveryServiceImpl {
    async fn list_devices(
        &self,
        _request: Request<ListDevicesRequest>,
    ) -> std::result::Result<Response<ListDevicesResponse>, Status> {
        let devices = list_devices()
            .map_err(|err| Status::internal(format!("Failed to list devices: {err}")))?;

        let response = ListDevicesResponse {
            devices: devices
                .into_iter()
                .map(|device| DeviceSummary {
                    name: device.name,
                    description: device.desc.unwrap_or_default(),
                    addresses: device
                        .addresses
                        .into_iter()
                        .map(|address| DeviceAddressSummary { addr: address.addr })
                        .collect(),
                })
                .collect(),
        };

        Ok(Response::new(response))
    }

    async fn list_device_previews(
        &self,
        request: Request<ListDevicePreviewsRequest>,
    ) -> std::result::Result<Response<ListDevicePreviewsResponse>, Status> {
        let preview_seconds = request.into_inner().preview_seconds;
        if preview_seconds == 0 {
            return Err(Status::invalid_argument(
                "preview_seconds must be greater than zero",
            ));
        }

        let duration = Duration::from_secs(preview_seconds);
        let previews = tokio::task::spawn_blocking(move || preview_devices(duration))
            .await
            .map_err(|err| Status::internal(format!("Preview worker failed: {err}")))?
            .map_err(|err| Status::internal(format!("Failed to preview devices: {err}")))?;

        let response = ListDevicePreviewsResponse {
            previews: previews
                .into_iter()
                .map(|preview| DevicePreview {
                    name: preview.name,
                    packets_seen: preview.packets_seen,
                    bytes_seen: preview.bytes_seen,
                    unsupported: preview.unsupported,
                    error: preview.error.unwrap_or_default(),
                })
                .collect(),
        };

        Ok(Response::new(response))
    }
}
