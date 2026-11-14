from __future__ import annotations

from foundgine import (
    FoundgineAbortError,
    FoundgineError,
    FoundgineHttpError,
    FoundgineResyncRequiredError,
    FoundgineRpcError,
    FoundgineSessionExpiredError,
    FoundgineWatchTimeoutError,
)


def test_hierarchy_matches_the_ts_classes() -> None:
    assert issubclass(FoundgineHttpError, FoundgineError)
    assert issubclass(FoundgineSessionExpiredError, FoundgineHttpError)
    assert issubclass(FoundgineResyncRequiredError, FoundgineHttpError)
    for cls in (FoundgineRpcError, FoundgineAbortError, FoundgineWatchTimeoutError):
        assert issubclass(cls, FoundgineError)
        assert not issubclass(cls, FoundgineHttpError)


def test_statuses() -> None:
    assert FoundgineHttpError(401).status == 401
    assert FoundgineSessionExpiredError().status == 404
    assert FoundgineResyncRequiredError().status == 410


def test_http_error_message_defaults_and_str() -> None:
    assert str(FoundgineHttpError(503)) == "HTTP 503"
    assert str(FoundgineHttpError(400, "bad")) == "bad"


def test_rpc_error_carries_code_and_data() -> None:
    e = FoundgineRpcError(-32601, "Method not found", {"m": "nope"})
    assert (e.code, e.message, e.data) == (-32601, "Method not found", {"m": "nope"})
    assert "-32601" in str(e)


def test_watch_timeout_carries_the_last_status() -> None:
    assert FoundgineWatchTimeoutError({"temporalStatus": "Running"}).last_status == {"temporalStatus": "Running"}
    assert FoundgineWatchTimeoutError().last_status is None


def test_error_text_never_contains_a_bearer_token() -> None:
    # The SDK never puts the token in an error; this pins that the constructors take no header data.
    e = FoundgineHttpError(401, "unauthenticated", body={"error": "unauthenticated"})
    assert "Bearer" not in repr(e) and "Bearer" not in str(e)
