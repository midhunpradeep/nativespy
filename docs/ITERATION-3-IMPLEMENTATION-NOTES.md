# Iteration 3 implementation notes

## Boundary

I3 moves the live WinForms correlation path from the test-only JSON Lines bridge to one authenticated Named Pipe session. The frozen `ClrAgentSession`, WinForms adapter, correlation coordinator, proof rules, and `Exact + SameManagedElement` semantics remain unchanged.

The target publishes exactly one UTF-8 bootstrap descriptor on stdout. stdout is not an RPC channel. The descriptor contains a random pipe name, a 32-byte base64url nonce, the exact target PID plus opaque decimal creation-time FILETIME identity, and the supported protocol range.

## Wire and transport

`NativeSpy.Protocol.Json` owns no transport or UI dependencies. It uses bounded UTF-8 JSON, camelCase properties, exact string enums, duplicate-property rejection, unknown-property rejection for protocol records, a 1 MiB effective frame limit, and a 4 MiB transport safety ceiling. Requests and responses use explicit versioned envelopes. Request IDs are canonical positive decimal `ulong` strings and are strictly increasing per connection.

`NativeSpy.Transport.NamedPipes` uses byte-mode framing with a 4-byte unsigned big-endian length. Payload buffers are rented from `ArrayPool<byte>` and returned after frame disposal. Writes are serialized per connection. The server creates an explicit protected ACL for the supplied account SID and validates the impersonated peer SID; no Users or Administrators ACE is added.

## Host and Client

`AgentHost` owns `Listening → Authenticating → Activating → Active → Closing → Closed`, accepts one successful client, creates `ClrAgentSession` only after authentication, freezes the handler registry, and sends the successful hello response only after composition succeeds. Authentication compares the nonce, exact process identity, protocol overlap, and account SID. Failed candidates receive only generic authentication errors and do not consume the nonce.

The Host keeps at most eight request slots. A monotonic Host budget controls dispatch and response commitment. Once UI target work has started, timeout does not abort that work; exactly one response branch is committed. Host failures use sanitized `InternalFailure` protocol errors, while target/dispatcher outcomes use `OperationErrorDto`.

`NamedPipeClientSession` owns one connection, session ID, negotiated version and limits, request sequence, pending-response map, and reader loop. It does not reconnect or open per-operation pipes. Terminal pipe loss is mapped to `TargetExited` only when the exact target instance is positively gone; otherwise it is `SessionClosed`. A successful evidence response may still contain an embedded operation error and remains a successful response branch.

`NativeSpy.Agent.Host.WinForms` is the only Host-to-WinForms composition assembly. Its dispatcher admits one active callback, checks the monotonic request context before dispatch and immediately before target work, and does not expose a generic public execution-context API.

## Deliberate exclusions

I3 does not add attach/injection, reflection, text inspection, leases, reconnect, WPF, ProviderAware behavior, multi-client sessions, or generic CLR inspection.
