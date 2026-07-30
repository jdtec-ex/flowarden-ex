mod cli;
mod geo;
mod output;
mod service;

use std::{
    process::ExitCode,
    sync::{
        Arc,
        atomic::{AtomicBool, Ordering},
    },
    time::Duration,
};

use cli::{CaptureArgs, Commands, CoreArgs, OutputFormat};
use flowarden_core::{
    analysis::LightDpiOptions,
    capture::{CaptureRuntime, RuntimeConfig, RuntimeMode},
    device::{DevicePreviewSummary, DeviceSummary, list_devices, preview_devices},
};
use flowarden_error::{ErrorType, OrErr, Result};
use output::{CliSignalOptions, emit_output, render_capture_output_with_signals};
use service::{CoreServiceOptions, run_core_service};

fn main() -> ExitCode {
    match run_sync() {
        Ok(()) => ExitCode::SUCCESS,
        Err(err) => {
            eprintln!("{err}");
            ExitCode::from(1)
        }
    }
}

fn run_sync() -> Result<()> {
    let cli = cli::parse()?;

    match cli.command {
        Commands::Core(args) => run_service(args),
        Commands::Devices { format, preview } => run_devices(format, preview),
        Commands::Capture(args) => run_capture(args),
    }
}

fn run_service(args: CoreArgs) -> Result<()> {
    let runtime = tokio::runtime::Runtime::new()
        .or_err(
            ErrorType::InternalError,
            "Failed to create Tokio runtime for Flowarden core mode",
        )
        .map_err(|e| e.into_core())?;

    runtime
        .block_on(run_core_service(CoreServiceOptions {
            bind: args.bind,
            syslog_target: args.syslog_target,
            syslog_proto: args.syslog_proto,
            syslog_emit_signals: args.syslog_emit_signals,
            syslog_emit_flows: args.syslog_emit_flows,
        }))
        .map_err(|e| e.into_core())
}

fn run_devices(format: OutputFormat, preview: Option<u64>) -> Result<()> {
    let devices = list_devices()?;
    let previews = preview.map(|seconds| preview_devices(Duration::from_secs(seconds.max(1))));
    let previews = match previews {
        Some(result) => Some(result?),
        None => None,
    };

    match format {
        OutputFormat::Table => print_devices_table(&devices, previews.as_deref()),
        OutputFormat::Json => print_devices_json(&devices, previews.as_deref())?,
    }

    Ok(())
}

fn run_capture(args: CaptureArgs) -> Result<()> {
    let (capture, output) = args.into_options()?;
    let bpf = capture.bpf.clone();
    let pcap_output_path = capture.pcap_output_path.clone();
    let duration_limit = capture.duration_limit;
    let light_dpi = LightDpiOptions::default()
        .with_sni_enabled(capture.sni_enabled)
        .with_sni_max_payload(capture.sni_max_payload.unwrap_or(512));
    let runtime = CaptureRuntime::new(
        capture.source,
        RuntimeConfig::forensic()
            .with_bpf(bpf.clone())
            .with_pcap_output_path(pcap_output_path.clone())
            .with_duration_limit(duration_limit)
            .with_snaplen(capture.snaplen)
            .with_light_dpi(light_dpi),
    );
    let stop_handle = runtime.stop_handle();
    install_ctrlc_handler(Arc::clone(&stop_handle))?;
    let report = runtime.run().map_err(|e| e.into_cli())?;
    let is_offline = matches!(report.mode, RuntimeMode::Offline);
    let signal_options = CliSignalOptions {
        is_offline,
        data_threshold_bytes: output.data_threshold_bytes,
        watched: output.watched.clone(),
        known_bad: output.known_bad.clone(),
    };
    let rendered = render_capture_output_with_signals(
        output.format,
        &report.tick_snapshots,
        &report.offline_gaps,
        &report.final_snapshot,
        output.top_n,
        &signal_options,
    )?;
    emit_output(&rendered, output.output_path.as_deref())?;

    let mode = if is_offline { "offline" } else { "live" };
    eprintln!(
        "capture completed: mode={mode}, link_type=\"{}\", packets_seen={}, bytes_seen={}, timed_out_ticks={}, stopped_by_request={}, bpf={:?}, format={:?}, top_n={}, output_path={:?}, pcap_output_path={:?}",
        report.link_type.full_print_on_one_line(),
        report.stats.packets_seen,
        report.stats.bytes_seen,
        report.timed_out_ticks,
        report.stopped_by_request,
        bpf,
        output.format,
        output.top_n,
        output.output_path,
        pcap_output_path
    );

    Ok(())
}

fn install_ctrlc_handler(stop_handle: Arc<AtomicBool>) -> Result<()> {
    ctrlc::set_handler(move || {
        stop_handle.store(true, Ordering::Relaxed);
    })
    .or_err(
        ErrorType::InternalError,
        "Failed to install Ctrl+C handler for capture runtime",
    )
    .map_err(|e| e.into_cli())
}

fn print_devices_json(
    devices: &[DeviceSummary],
    previews: Option<&[DevicePreviewSummary]>,
) -> Result<()> {
    let json = serde_json::to_string_pretty(&serde_json::json!({
        "devices": devices,
        "previews": previews,
    }))
    .or_err(
        ErrorType::InternalError,
        "Failed to serialize devices to JSON",
    )
    .map_err(|e| e.into_cli())?;
    println!("{json}");
    Ok(())
}

fn print_devices_table(devices: &[DeviceSummary], previews: Option<&[DevicePreviewSummary]>) {
    for device in devices {
        println!("{}", device.name);

        if let Some(desc) = &device.desc {
            println!("  desc: {desc}");
        }

        if device.addresses.is_empty() {
            println!("  addresses: -");
        } else {
            println!("  addresses:");
            for address in &device.addresses {
                println!("    - {}", address.addr);
            }
        }

        if let Some(previews) = previews
            && let Some(preview) = previews.iter().find(|preview| preview.name == device.name)
        {
            println!(
                "  preview: packets={}, bytes={}, unsupported={}",
                preview.packets_seen, preview.bytes_seen, preview.unsupported
            );
            if let Some(error) = &preview.error {
                println!("  preview_error: {error}");
            }
        }
    }
}
