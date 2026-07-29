#![warn(clippy::all)]
//! The library to provide the struct to represent errors in flowarden.

pub use std::error::Error as StdError;
use std::result::Result as StdResult;

use std::fmt;
use std::fmt::Debug;

mod thin_str;
pub use thin_str::ThinStr;

pub type BError = Box<Error>;
pub type Result<T, E = BError> = StdResult<T, E>;

/// The struct that represents an error
#[derive(Debug)]
pub struct Error {
    /// the type of error
    pub etype: ErrorType,
    /// the source of error
    pub esource: ErrorSource,
    /// chain to the cause of this error
    pub cause: Option<Box<dyn StdError + Send + Sync>>,
    /// an arbitrary string that explains the context when the error happens
    pub context: Option<ThinStr>,
}

/// The source of the error
#[derive(Debug, PartialEq, Eq, Clone)]
pub enum ErrorSource {
    /// The error is caused by the network traffic capture.
    Network,
    /// The error is caused by the cli client
    CliTerminal,
    /// The error is caused by the communication between the cli client and the core service
    UiCommunication,
    /// The error is caused by the Flowarden core service
    CoreService,
    /// The error is caused by internal logic
    Internal,
    /// Error source unknown or to be set
    Unset,
}

impl ErrorSource {
    /// for displaying the error source
    pub fn as_str(&self) -> &'static str {
        match self {
            Self::Network => "Network",
            Self::CliTerminal => "Cli Terminal",
            Self::UiCommunication => "Ui Communication",
            Self::Internal => "Internal",
            Self::CoreService => "Core Service",
            Self::Unset => "",
        }
    }
}

/// Predefined type of errors
#[derive(Debug, PartialEq, Eq, Clone)]
pub enum ErrorType {
    ConnectTimedout,
    ConnectRefused,
    ConnectNoRoute,
    ConnectionClosed,
    ConnectError,
    AcceptError,
    SocketError,
    ReadError,
    WriteError,
    ReadTimedout,
    WriteTimedout,
    FileOpenError,
    FileCreateError,
    FileReadError,
    FileWriteError,
    InternalError,
    UnknownError,
    PacketReadError,
    InvalidInput,
    PermissionDenied,
    UnsupportedLinkType,
    PacketDecodeError,
    FilterApplyError,
    DeviceNotFound,
    DeviceListError,
    CaptureStartError,
    CaptureStopError,
    HTTPStatus(u16),
    Custom(&'static str),
    CustomCode(&'static str, u16),
}

impl ErrorType {
    pub const fn new(name: &'static str) -> Self {
        ErrorType::Custom(name)
    }

    pub const fn new_code(name: &'static str, code: u16) -> Self {
        ErrorType::CustomCode(name, code)
    }

    /// for displaying the error type
    pub fn as_str(&self) -> &'static str {
        match self {
            ErrorType::ConnectTimedout => "ConnectTimedout",
            ErrorType::ConnectRefused => "ConnectRefused",
            ErrorType::ConnectNoRoute => "ConnectNoRoute",
            ErrorType::ConnectError => "ConnectError",
            ErrorType::ConnectionClosed => "ConnectionClosed",
            ErrorType::AcceptError => "AcceptError",
            ErrorType::SocketError => "SocketError",
            ErrorType::ReadError => "ReadError",
            ErrorType::WriteError => "WriteError",
            ErrorType::PacketReadError => "PacketReadError",
            ErrorType::ReadTimedout => "ReadTimedout",
            ErrorType::WriteTimedout => "WriteTimedout",
            ErrorType::FileOpenError => "FileOpenError",
            ErrorType::FileCreateError => "FileCreateError",
            ErrorType::FileReadError => "FileReadError",
            ErrorType::FileWriteError => "FileWriteError",
            ErrorType::InternalError => "InternalError",
            ErrorType::UnknownError => "UnknownError",
            ErrorType::InvalidInput => "InvalidInput",
            ErrorType::PermissionDenied => "PermissionDenied",
            ErrorType::UnsupportedLinkType => "UnsupportedLinkType",
            ErrorType::PacketDecodeError => "PacketDecodeError",
            ErrorType::FilterApplyError => "FilterApplyError",
            ErrorType::DeviceNotFound => "DeviceNotFound",
            ErrorType::DeviceListError => "DeviceListError",
            ErrorType::CaptureStartError => "CaptureStartError",
            ErrorType::CaptureStopError => "CaptureStopError",
            ErrorType::HTTPStatus(_) => "HTTPStatus",
            ErrorType::Custom(s) => s,
            ErrorType::CustomCode(s, _) => s,
        }
    }
}

impl Error {
    #[inline]
    pub fn create(
        etype: ErrorType,
        esource: ErrorSource,
        context: Option<ThinStr>,
        cause: Option<Box<dyn StdError + Send + Sync>>,
    ) -> BError {
        Box::new(Error {
            etype,
            esource,
            cause,
            context,
        })
    }

    #[inline]
    fn do_new(e: ErrorType, s: ErrorSource) -> BError {
        Self::create(e, s, None, None)
    }

    /// Create an error with the given type
    #[inline]
    pub fn new(e: ErrorType) -> BError {
        Self::do_new(e, ErrorSource::Unset)
    }

    /// Create an error with the given type, a context string and the causing error.
    /// This method is usually used when there the error is caused by another error.
    /// Choose carefully between simply surfacing the causing error versus Because() here.
    /// Only use Because() when there is extra context that is not capture by
    /// the causing error itself.
    #[inline]
    pub fn because<S: Into<ThinStr>, E: Into<Box<dyn StdError + Send + Sync>>>(
        e: ErrorType,
        context: S,
        cause: E,
    ) -> BError {
        Self::create(
            e,
            ErrorSource::Unset,
            Some(context.into()),
            Some(cause.into()),
        )
    }

    /// Short for Err(Self::because)
    #[inline]
    pub fn e_because<T, S: Into<ThinStr>, E: Into<Box<dyn StdError + Send + Sync>>>(
        e: ErrorType,
        context: S,
        cause: E,
    ) -> Result<T> {
        Err(Self::because(e, context, cause))
    }

    /// Create an error with context but no direct causing error
    #[inline]
    pub fn explain<S: Into<ThinStr>>(e: ErrorType, context: S) -> BError {
        Self::create(e, ErrorSource::Unset, Some(context.into()), None)
    }

    /// Short for Err(Self::explain)
    #[inline]
    pub fn e_explain<T, S: Into<ThinStr>>(e: ErrorType, context: S) -> Result<T> {
        Err(Self::explain(e, context))
    }

    #[inline]
    pub fn new_network(e: ErrorType) -> BError {
        Self::do_new(e, ErrorSource::Network)
    }

    #[inline]
    pub fn new_in(e: ErrorType) -> BError {
        Self::do_new(e, ErrorSource::Internal)
    }

    /// Create a new custom error with the static string
    #[inline]
    pub fn new_str(s: &'static str) -> BError {
        Self::do_new(ErrorType::Custom(s), ErrorSource::Unset)
    }

    // the err_* functions are the same as new_* but return a Result<T>
    #[inline]
    pub fn err<T>(e: ErrorType) -> Result<T> {
        Err(Self::new(e))
    }

    #[inline]
    pub fn err_network<T>(e: ErrorType) -> Result<T> {
        Err(Self::new_network(e))
    }

    #[inline]
    pub fn err_in<T>(e: ErrorType) -> Result<T> {
        Err(Self::new_in(e))
    }

    pub fn etype(&self) -> &ErrorType {
        &self.etype
    }

    pub fn esource(&self) -> &ErrorSource {
        &self.esource
    }

    pub fn reason_str(&self) -> &str {
        self.etype.as_str()
    }

    pub fn source_str(&self) -> &str {
        self.esource.as_str()
    }

    pub fn as_network(&mut self) {
        self.esource = ErrorSource::Network;
    }

    pub fn as_cli(&mut self) {
        self.esource = ErrorSource::CliTerminal;
    }

    pub fn as_in(&mut self) {
        self.esource = ErrorSource::Internal;
    }

    pub fn as_core(&mut self) {
        self.esource = ErrorSource::CoreService;
    }

    pub fn into_network(mut self: BError) -> BError {
        self.as_network();
        self
    }

    pub fn into_cli(mut self: BError) -> BError {
        self.as_cli();
        self
    }

    pub fn into_in(mut self: BError) -> BError {
        self.as_in();
        self
    }

    pub fn into_core(mut self: BError) -> BError {
        self.as_core();
        self
    }

    pub fn into_err<T>(self: BError) -> Result<T> {
        Err(self)
    }

    pub fn set_cause<C: Into<Box<dyn StdError + Send + Sync>>>(&mut self, cause: C) {
        self.cause = Some(cause.into());
    }

    pub fn set_context<T: Into<ThinStr>>(&mut self, context: T) {
        self.context = Some(context.into());
    }

    /// Create a new error from self, with the same type and source and put self as the cause
    /// This function is less verbose than `Because`. But it only work for [Error] while
    /// `Because` works for all types of errors who implement [std::error::Error] trait.
    pub fn more_context<T: Into<ThinStr>>(self: BError, context: T) -> BError {
        let esource = self.esource.clone();
        let mut e = Self::because(self.etype.clone(), context, self);
        e.esource = esource;
        e
    }

    // Display error but skip the duplicate elements from the error in previous hop
    fn chain_display(&self, previous: Option<&Error>, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        if previous.map(|p| p.esource != self.esource).unwrap_or(true) {
            write!(f, "{}", self.esource.as_str())?
        }
        if previous.map(|p| p.etype != self.etype).unwrap_or(true) {
            write!(f, " {}", self.etype.as_str())?
        }

        if let Some(c) = self.context.as_ref() {
            write!(f, " context: {}", c)?;
        }
        if let Some(c) = self.cause.as_ref() {
            if let Some(e) = c.downcast_ref::<BError>() {
                write!(f, " cause: ")?;
                e.chain_display(Some(self), f)
            } else {
                write!(f, " cause: {}", c)
            }
        } else {
            Ok(())
        }
    }

    /// Return the ErrorType of the root Error
    pub fn root_etype(&self) -> &ErrorType {
        self.cause.as_ref().map_or(&self.etype, |c| {
            // Stop the recursion if the cause is not Error
            c.downcast_ref::<BError>()
                .map_or(&self.etype, |e| e.root_etype())
        })
    }

    pub fn root_cause(&self) -> &(dyn StdError + Send + Sync + 'static) {
        self.cause.as_deref().map_or(self, |c| {
            c.downcast_ref::<BError>().map_or(c, |e| e.root_cause())
        })
    }
}

impl fmt::Display for Error {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        self.chain_display(None, f)
    }
}

impl StdError for Error {}

/// Helper trait to add more context to a given error
pub trait Context<T> {
    /// Wrap the `Err(E)` in [Result] with more context, the existing E will be the cause.
    ///
    /// This is a shortcut for map_err() + more_context()
    fn err_context<C: Into<ThinStr>, F: FnOnce() -> C>(self, context: F) -> Result<T, BError>;
}

impl<T> Context<T> for Result<T, BError> {
    fn err_context<C: Into<ThinStr>, F: FnOnce() -> C>(self, context: F) -> Result<T, BError> {
        self.map_err(|e| e.more_context(context()))
    }
}

/// Helper trait to chain errors with context
pub trait OrErr<T, E> {
    /// Wrap the E in [Result] with new [ErrorType] and context, the existing E will be the cause.
    ///
    /// This is a shortcut for map_err() + because()
    fn or_err(self, et: ErrorType, context: &'static str) -> Result<T, BError>
    where
        E: Into<Box<dyn StdError + Send + Sync>>;

    /// Similar to or_err(), but takes a closure, which is useful for constructing String.
    fn or_err_with<C: Into<ThinStr>, F: FnOnce() -> C>(
        self,
        et: ErrorType,
        context: F,
    ) -> Result<T, BError>
    where
        E: Into<Box<dyn StdError + Send + Sync>>;

    /// Replace the E in [Result] with a new [Error] generated from the current error
    ///
    /// This is useful when the current error cannot move out of scope. This is a shortcut for map_err() + explain().
    fn explain_err<C: Into<ThinStr>, F: FnOnce(E) -> C>(
        self,
        et: ErrorType,
        context: F,
    ) -> Result<T, BError>;

    /// Similar to or_err() but just to surface errors that are not [Error] (where `?` cannot be used directly).
    ///
    /// or_err()/or_err_with() are still preferred because they make the error more readable and traceable.
    fn or_fail(self) -> Result<T>
    where
        E: Into<Box<dyn StdError + Send + Sync>>;
}

impl<T, E> OrErr<T, E> for Result<T, E> {
    fn or_err(self, et: ErrorType, context: &'static str) -> Result<T, BError>
    where
        E: Into<Box<dyn StdError + Send + Sync>>,
    {
        self.map_err(|e| Error::because(et, context, e))
    }

    fn or_err_with<C: Into<ThinStr>, F: FnOnce() -> C>(
        self,
        et: ErrorType,
        context: F,
    ) -> Result<T, BError>
    where
        E: Into<Box<dyn StdError + Send + Sync>>,
    {
        self.map_err(|e| Error::because(et, context(), e))
    }

    fn explain_err<C: Into<ThinStr>, F: FnOnce(E) -> C>(
        self,
        et: ErrorType,
        exp: F,
    ) -> Result<T, BError> {
        self.map_err(|e| Error::explain(et, exp(e)))
    }

    fn or_fail(self) -> Result<T, BError>
    where
        E: Into<Box<dyn StdError + Send + Sync>>,
    {
        self.map_err(|e| Error::because(ErrorType::InternalError, "", e))
    }
}

/// Helper trait to convert an [Option] to an [Error] with context.
pub trait OkOrErr<T> {
    fn or_err(self, et: ErrorType, context: &'static str) -> Result<T, BError>;

    fn or_err_with<C: Into<ThinStr>, F: FnOnce() -> C>(
        self,
        et: ErrorType,
        context: F,
    ) -> Result<T, BError>;
}

impl<T> OkOrErr<T> for Option<T> {
    /// Convert the [Option] to a new [Error] with [ErrorType] and context if None, Ok otherwise.
    ///
    /// This is a shortcut for .ok_or(Error::explain())
    fn or_err(self, et: ErrorType, context: &'static str) -> Result<T, BError> {
        self.ok_or(Error::explain(et, context))
    }

    /// Similar to to_err(), but takes a closure, which is useful for constructing String.
    fn or_err_with<C: Into<ThinStr>, F: FnOnce() -> C>(
        self,
        et: ErrorType,
        context: F,
    ) -> Result<T, BError> {
        self.ok_or_else(|| Error::explain(et, context()))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_chain_of_error() {
        let e1 = Error::new(ErrorType::InternalError);
        let mut e2 = Error::new(ErrorType::HTTPStatus(400));
        e2.set_cause(e1);
        assert_eq!(format!("{}", e2), " HTTPStatus cause:  InternalError");
        assert_eq!(e2.root_etype().as_str(), "InternalError");

        let e3 = Error::new(ErrorType::InternalError);
        let e4 = Error::because(ErrorType::HTTPStatus(400), "test", e3);
        assert_eq!(
            format!("{}", e4),
            " HTTPStatus context: test cause:  InternalError"
        );
        assert_eq!(e4.root_etype().as_str(), "InternalError");
    }

    #[test]
    fn test_error_context() {
        let mut e1 = Error::new(ErrorType::InternalError);
        e1.set_context(format!("{} {}", "my", "context"));
        assert_eq!(format!("{}", e1), " InternalError context: my context");
    }

    #[test]
    fn test_context_trait() {
        let e1: Result<(), BError> = Err(Error::new(ErrorType::InternalError));
        let e2 = e1.err_context(|| "another");
        assert_eq!(
            format!("{}", e2.unwrap_err()),
            " InternalError context: another cause: "
        );
    }

    #[test]
    fn test_cause_trait() {
        let e1: Result<(), BError> = Err(Error::new(ErrorType::InternalError));
        let e2 = e1.or_err(ErrorType::HTTPStatus(400), "another");
        assert_eq!(
            format!("{}", e2.unwrap_err()),
            " HTTPStatus context: another cause:  InternalError"
        );
    }

    #[test]
    fn test_option_some_ok() {
        let m = Some(2);
        let o = m.or_err(ErrorType::InternalError, "some is not an error!");
        assert_eq!(2, o.unwrap());

        let o = m.or_err_with(ErrorType::InternalError, || "some is not an error!");
        assert_eq!(2, o.unwrap());
    }

    #[test]
    fn test_option_none_err() {
        let m: Option<i32> = None;
        let e1 = m.or_err(ErrorType::InternalError, "none is an error!");
        assert_eq!(
            format!("{}", e1.unwrap_err()),
            " InternalError context: none is an error!"
        );

        let e1 = m.or_err_with(ErrorType::InternalError, || "none is an error!");
        assert_eq!(
            format!("{}", e1.unwrap_err()),
            " InternalError context: none is an error!"
        );
    }

    #[test]
    fn test_into() {
        fn other_error() -> Result<(), &'static str> {
            Err("oops")
        }

        fn surface_err() -> Result<()> {
            other_error().or_fail()?;
            Ok(())
        }

        let e = surface_err().unwrap_err();
        assert_eq!(format!("{}", e), " InternalError context:  cause: oops");
    }
}
