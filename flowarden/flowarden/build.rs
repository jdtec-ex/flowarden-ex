fn main() -> Result<(), Box<dyn std::error::Error>> {
    let protoc = protoc_bin_vendored::protoc_bin_path()?;
    unsafe {
        std::env::set_var("PROTOC", protoc);
    }

    let proto_files = [
        "../proto/flowarden/health.proto",
        "../proto/flowarden/discovery.proto",
        "../proto/flowarden/control.proto",
        "../proto/flowarden/projection.proto",
    ];

    tonic_prost_build::configure()
        .build_server(true)
        // Clients enabled for in-process e2e tests (syslog / control / projection).
        .build_client(true)
        .compile_protos(&proto_files, &["../proto"])?;

    Ok(())
}
