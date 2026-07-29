use std::{net::SocketAddr, path::PathBuf, time::Duration};

use clap::{Args, Parser, Subcommand, ValueEnum};
use flowarden_core::capture::CaptureSource;
use flowarden_error::{Error, ErrorType, Result};

#[derive(Debug, Clone, Parser)]
#[command(
    name = "flowarden",
    version,
    about = "Flowarden",
    long_about = "Flowarden is a network traffic monitoring and analysis tool built in Rust."
)]
pub struct Cli {
    #[command(subcommand)]
    pub command: Commands,
}

#[derive(Debug, Clone, Subcommand)]
pub enum Commands {
    /// List available capture devices
    Devices {
        #[arg(long, value_enum, default_value_t = OutputFormat::Table)]
        format: OutputFormat,
        #[arg(long, help = "Duration in seconds to preview traffic on each device")]
        preview: Option<u64>,
    },
    /// Run Flowarden as a resident core process for the desktop UI
    #[command(alias = "service")]
    Core(CoreArgs),
    /// Start a capture session
    Capture(CaptureArgs),
}

#[derive(Debug, Clone, Args)]
pub struct CoreArgs {
    #[arg(long)]
    pub bind: SocketAddr,
}

#[derive(Debug, Clone, Args)]
pub struct CaptureArgs {
    #[arg(long, conflicts_with = "read")]
    pub device: Option<String>,
    #[arg(long, conflicts_with = "device")]
    pub read: Option<PathBuf>,
    #[arg(long)]
    pub bpf: Option<String>,
    #[arg(long, value_enum, default_value_t = OutputFormat::Table)]
    pub format: OutputFormat,
    #[arg(
        long,
        help = "Path to output file for capture summary (JSON or table text based on --format)"
    )]
    pub output: Option<PathBuf>,
    #[arg(long, help = "Path to output pcap file")]
    pub pcap_out: Option<PathBuf>,
    #[arg(long)]
    pub duration: Option<u64>,
    #[arg(long, default_value_t = 10)]
    pub top: usize,
    /// Data threshold for findings (bytes). Default 50_000_000.
    #[arg(long, default_value_t = 50_000_000)]
    pub data_threshold: u64,
    /// Comma-separated watch patterns (host, service:https, process:X, sni:y).
    #[arg(long, value_delimiter = ',')]
    pub watch: Vec<String>,
    /// Comma-separated known-bad patterns.
    #[arg(long, value_delimiter = ',')]
    pub known_bad: Vec<String>,
    /// Live capture snaplen in bytes (default 512 for Light DPI). Ignored when --pcap-out is set.
    #[arg(long)]
    pub snaplen: Option<i32>,
    /// Disable TLS ClientHello SNI extraction.
    #[arg(long, default_value_t = false)]
    pub no_sni: bool,
    /// Max TCP payload bytes inspected for SNI (default 512).
    #[arg(long)]
    pub sni_max_payload: Option<usize>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, ValueEnum)]
pub enum OutputFormat {
    Table,
    Json,
}

#[derive(Debug, Clone)]
pub struct CaptureOptions {
    pub source: CaptureSource,
    pub bpf: Option<String>,
    pub pcap_output_path: Option<PathBuf>,
    pub duration_limit: Option<Duration>,
    pub snaplen: Option<i32>,
    pub sni_enabled: bool,
    pub sni_max_payload: Option<usize>,
}

#[derive(Debug, Clone)]
pub struct OutputOptions {
    pub format: OutputFormat,
    pub output_path: Option<PathBuf>,
    pub top_n: usize,
    pub data_threshold_bytes: u64,
    pub watched: Vec<String>,
    pub known_bad: Vec<String>,
}

impl CaptureArgs {
    pub fn into_options(self) -> Result<(CaptureOptions, OutputOptions)> {
        if let (Some(output), Some(pcap_out)) = (&self.output, &self.pcap_out)
            && output == pcap_out
        {
            return Error::explain(
                ErrorType::InvalidInput,
                "--output and --pcap-out must point to different files",
            )
            .into_cli()
            .into_err();
        }

        let source = match (self.device, self.read) {
            (Some(device), None) => {
                CaptureSource::from_device_name(&device).map_err(|e| e.into_cli())?
            }
            (None, Some(path)) => CaptureSource::from_file_path(path).map_err(|e| e.into_cli())?,
            (None, None) => {
                return Error::explain(
                    ErrorType::InvalidInput,
                    "Either --device or --read must be provided",
                )
                .into_cli()
                .into_err();
            }
            (Some(_), Some(_)) => {
                return Error::explain(
                    ErrorType::InvalidInput,
                    "--device and --read cannot be used together",
                )
                .into_cli()
                .into_err();
            }
        };

        if self.top == 0 {
            return Error::explain(ErrorType::InvalidInput, "--top must be greater than zero")
                .into_cli()
                .into_err();
        }

        let capture = CaptureOptions {
            source,
            bpf: self.bpf,
            pcap_output_path: self.pcap_out,
            duration_limit: self.duration.map(Duration::from_secs),
            snaplen: self.snaplen,
            sni_enabled: !self.no_sni,
            sni_max_payload: self.sni_max_payload,
        };

        let output = OutputOptions {
            format: self.format,
            output_path: self.output,
            top_n: self.top,
            data_threshold_bytes: self.data_threshold,
            watched: self
                .watch
                .into_iter()
                .map(|s| s.trim().to_string())
                .filter(|s| !s.is_empty())
                .collect(),
            known_bad: self
                .known_bad
                .into_iter()
                .map(|s| s.trim().to_string())
                .filter(|s| !s.is_empty())
                .collect(),
        };

        Ok((capture, output))
    }
}

pub fn parse() -> Result<Cli> {
    match Cli::try_parse() {
        Ok(cli) => Ok(cli),
        Err(err) => {
            use clap::error::ErrorKind;

            match err.kind() {
                ErrorKind::DisplayHelp | ErrorKind::DisplayVersion => {
                    print!("{err}");
                    std::process::exit(0);
                }
                _ => Err(Error::because(
                    ErrorType::InvalidInput,
                    "Failed to parse command line arguments",
                    err,
                )
                .into_cli()),
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn capture_requires_source() {
        let args = CaptureArgs {
            device: None,
            read: None,
            bpf: None,
            format: OutputFormat::Table,
            output: None,
            pcap_out: None,
            duration: None,
            top: 20,
            data_threshold: 50_000_000,
            watch: Vec::new(),
            known_bad: Vec::new(),
            snaplen: None,
            no_sni: false,
            sni_max_payload: None,
        };

        let err = args.into_options().unwrap_err();
        assert_eq!(err.reason_str(), "InvalidInput");
        assert_eq!(err.source_str(), "Cli Terminal");
    }

    #[test]
    fn capture_rejects_same_output_and_pcap_output_path() {
        let path = PathBuf::from("/tmp/flowarden-out");
        let args = CaptureArgs {
            device: Some("lo0".to_string()),
            read: None,
            bpf: None,
            format: OutputFormat::Json,
            output: Some(path.clone()),
            pcap_out: Some(path),
            duration: Some(1),
            top: 20,
            data_threshold: 50_000_000,
            watch: Vec::new(),
            known_bad: Vec::new(),
            snaplen: None,
            no_sni: false,
            sni_max_payload: None,
        };

        let err = args.into_options().unwrap_err();
        assert_eq!(err.reason_str(), "InvalidInput");
        assert_eq!(err.source_str(), "Cli Terminal");
    }

    #[test]
    fn devices_preview_accepts_positive_duration() {
        let cli = Cli::try_parse_from(["flowarden", "devices", "--preview", "2"]).unwrap();
        match cli.command {
            Commands::Devices { preview, .. } => assert_eq!(preview, Some(2)),
            Commands::Core(_) | Commands::Capture(_) => panic!("expected devices command"),
        }
    }

    #[test]
    fn core_command_accepts_bind() {
        let cli = Cli::try_parse_from(["flowarden", "core", "--bind", "127.0.0.1:39092"]).unwrap();

        match cli.command {
            Commands::Core(args) => assert_eq!(args.bind.to_string(), "127.0.0.1:39092"),
            Commands::Devices { .. } | Commands::Capture(_) => panic!("expected core command"),
        }
    }

    #[test]
    fn core_command_requires_bind() {
        let err = Cli::try_parse_from(["flowarden", "core"]).unwrap_err();
        assert_eq!(err.kind(), clap::error::ErrorKind::MissingRequiredArgument);
    }

    #[test]
    fn service_alias_still_parses() {
        let cli =
            Cli::try_parse_from(["flowarden", "service", "--bind", "127.0.0.1:39092"]).unwrap();

        match cli.command {
            Commands::Core(args) => assert_eq!(args.bind.to_string(), "127.0.0.1:39092"),
            Commands::Devices { .. } | Commands::Capture(_) => panic!("expected core alias"),
        }
    }
}
