# Iteration 3 implementation notes

## Boundary

I3 moves the live WinForms correlation path from the test-only JSON Lines bridge to one authenticated Named Pipe session. The frozen `ClrAgentSession`, WinForms adapter, correlation coordinator, proof rules, and `Exact + SameManagedElement` semantics remain unchanged.

The target publishes exactly one UTF-8 bootstrap descriptor on stdout. stdout is not an RPC channel. The descriptor contains a random pipe name, a 32-byte base64url nonce, the exact target PID plus opaque decimal creation-time FILETIME identity, and the supported protocol range.

## Wire and transport

`NativeSpy.Protocol.Json` owns no transport or UI dependencies. It uses bounded UTF-8 JSON, camelCase properties, exact string enums, duplicate-property rejection, unknown-property rejection for protocol records, a fixed maximum JSON depth of 64, a 1 MiB effective frame limit, and a 4 MiB transport safety ceiling. Requests and responses use explicit versioned envelopes with generic non-null JSON payloads. Request IDs are canonical positive decimal `ulong` strings and are strictly increasing per connection. `NativeSpy.Protocol.Common.ProtocolOperationNames` is the shared operation contract; `hello`, `helloResponse`, `error`, `request`, `response`, and `shutdown` are reserved.

`NativeSpy.Transport.NamedPipes` uses byte-mode framing with a 4-byte unsigned big-endian length. Payload buffers are rented from `ArrayPool<byte>` and returned after frame disposal. Writes are serialized per connection. The server eagerly creates an explicit protected ACL for the supplied account SID before Host startup reports `Listening`, and validates the impersonated peer SID; no Users or Administrators ACE is added.

## Host and Client

`AgentHost` owns `Listening → Authenticating → Activating → Active → Closing → Closed`, accepts one successful client, creates `ClrAgentSession` only after authentication, freezes the handler registry against fixed declarations, and sends the successful hello response only after composition succeeds. Operation names use the shared canonical contract and the activated registry must match it exactly. Authentication compares the nonce, exact process identity, protocol overlap, and account SID. Failed candidates receive only generic authentication errors and do not consume the nonce; listener ACL/bind failures fail startup before `Listening`, and bootstrap expiry closes the Host.

The Host keeps at most eight request slots. A monotonic Host budget controls dispatch and response preparation. Once UI target work has started, timeout does not abort that work; complete, size-validated serialization entering the writer path is the logical response commitment, exactly one response branch is committed, and late work retains its slot until preparation finishes. Host failures use sanitized `InternalFailure` protocol errors, while target/dispatcher outcomes use `OperationErrorDto`. Physical delivery failure after commitment is terminal session closure, not a second timeout response.

`NamedPipeClientSession` owns one connection, session ID, negotiated version and fixed limits, request sequence, pending-response map, and reader loop. It does not reconnect or open per-operation pipes. Caller cancellation abandons a request without removing its pending slot; the slot is released only by the late response or terminal session cleanup, so abandonment is bounded by the negotiated outstanding-request limit. Terminal pipe loss is mapped to `TargetExited` only when the exact target instance is positively gone; otherwise it is `SessionClosed`. A successful evidence response may still contain an embedded operation error and remains a successful response branch.

`NativeSpy.Agent.Host.WinForms` is the only Host-to-WinForms composition assembly. Its dispatcher admits one active callback, checks the monotonic request context before dispatch and immediately before target work, and does not expose a generic public execution-context API.

## Deliberate exclusions

I3 does not add attach/injection, reflection, text inspection, leases, reconnect, WPF, ProviderAware behavior, multi-client sessions, or generic CLR inspection.
