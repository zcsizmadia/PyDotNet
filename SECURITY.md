# Security policy

## Supported versions

Security fixes land on the latest released minor version. Older versions are not patched.

| Version | Supported |
|---|---|
| 1.2.x | Yes |
| 1.1.x and earlier | No — upgrade to 1.2.x |

## Reporting a vulnerability

**Please do not open a public issue for a security problem.**

Report it through GitHub's private vulnerability reporting: open the
[Security tab](https://github.com/zcsizmadia/PyDotNet/security/advisories/new) and choose
*Report a vulnerability*. That creates a private advisory only you and the maintainers can
see, and it stays private until a fix is published.

If that is unavailable to you, contact the maintainer through their
[GitHub profile](https://github.com/zcsizmadia) and ask for a private channel before sending
any detail.

### What to include

- The affected version, operating system, .NET target framework and Python version
- The output of `PyRuntime.EffectiveConfiguration?.ToString()`, which records the
  interpreter that was actually loaded
- What an attacker can achieve, and the smallest reproduction you have

### What to expect

An acknowledgement within a few days, an assessment of whether the report is something
PyDotNet can fix, and credit in the advisory unless you would rather stay anonymous. This is
a volunteer-maintained project, so please allow reasonable time before disclosing publicly.

## Scope

PyDotNet loads CPython into the host process and calls across the boundary directly. That
shapes what counts as a vulnerability here.

**In scope**

- Memory safety in the interop layer — reference-counting errors, use-after-free, buffer
  handling in the zero-copy paths
- A configuration option not doing what it says: isolation that does not isolate, a virtual
  environment that resolves to the wrong interpreter, `sys.path` entries placed contrary to
  the requested precedence
- Anything that lets Python code reach further into the host process than the documented API
  allows

**Out of scope**

- Vulnerabilities in CPython itself — report those to the
  [Python Security Response Team](https://www.python.org/dev/security/)
- Vulnerabilities in third-party Python packages such as numpy or pandas — report those to
  their maintainers
- **Executing untrusted Python code.** PyDotNet runs Python in your process with your
  privileges, by design. Sandboxing untrusted code is not something it attempts, and
  `PyIsolationOptions` is not a security boundary — it controls which *environment* the
  interpreter reads, not what code can do once running.
