use std::fmt;

/// A data struct that holds either immutable string(with ownership) or reference to static str.
/// Compared to String or `Box<str>`, it avoids memory allocation on static str.
#[derive(Debug, PartialEq, Eq, Clone)]
pub enum ThinStr {
    Static(&'static str),
    Owned(Box<str>),
}

impl ThinStr {
    #[inline]
    pub fn as_str(&self) -> &str {
        match self {
            ThinStr::Static(s) => s,
            ThinStr::Owned(s) => s.as_ref(),
        }
    }

    pub fn is_owned(&self) -> bool {
        match self {
            ThinStr::Static(_) => false,
            ThinStr::Owned(_) => true,
        }
    }
}

impl fmt::Display for ThinStr {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{}", self.as_str())
    }
}

impl From<&'static str> for ThinStr {
    fn from(s: &'static str) -> Self {
        ThinStr::Static(s)
    }
}

impl From<String> for ThinStr {
    fn from(s: String) -> Self {
        ThinStr::Owned(s.into_boxed_str())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_static_vs_owned() {
        let s: ThinStr = "test".into();
        assert!(!s.is_owned());
        let s: ThinStr = "test".to_string().into();
        assert!(s.is_owned());
    }
}
