"""Moondream2 vision worker.

Spawned by TradeCopilot's MoondreamVisionEngine. Speaks newline-delimited JSON
over stdin/stdout so the .NET host never has to parse model output directly.

Protocol (one JSON object per line):

  host  -> worker   stdin : {"image": "<abs path to png>", "prompt": "<question>"}
  worker -> host    stdout: {"status": "loading"}              on startup
                    {"status": "ready"}                        once the model can answer
                    {"status": "error", "message": "<reason>"} if the model cannot be used
                    {"answer": "<text>"}                       in reply to a request
                    {"error": "<reason>"}                      if a request fails

The worker checks for the classic Moondream module API
(encode_image / answer_question / tokenizer) and reports a clear error when the
installed client only exposes the cloud query/caption API instead. That keeps the
host's ``Enabled`` flag honest instead of hanging or crashing.
"""

import json
import sys
import traceback

# Capture the real stdout before anything can print to it. Protocol messages go to
# this handle; any library chatter (some moondream builds print on import) is
# redirected to stderr so it cannot corrupt the JSON stream the host reads.
_OUT = sys.stdout


def _emit(obj):
    _OUT.write(json.dumps(obj) + "\n")
    _OUT.flush()


def _silence_stdout():
    sys.stdout = sys.stderr


def _load_model():
    """Import and build the Moondream model, or raise a descriptive error."""
    try:
        import moondream  # noqa: F401
    except Exception as exc:
        raise RuntimeError("moondream package not installed: %s" % exc) from exc

    try:
        from moondream import vl
    except Exception as exc:
        raise RuntimeError("moondream.vl not available: %s" % exc) from exc

    try:
        model = vl()
    except Exception as exc:
        raise RuntimeError("moondream.vl() failed to initialise: %s" % exc) from exc

    # The classic local API exposes encode_image / answer_question / tokenizer.
    # The cloud client (>= 2.x) only exposes query/caption, which this worker
    # cannot drive, so treat that as "model unavailable".
    missing = [
        name
        for name in ("encode_image", "answer_question", "tokenizer")
        if not hasattr(model, name)
    ]
    if missing:
        raise RuntimeError(
            "installed moondream client lacks the local API (%s); the classic "
            "encode_image/answer_question/tokenizer module is required"
            % ", ".join(missing)
        )

    return model


def _answer(model, image_path, prompt):
    with open(image_path, "rb") as handle:
        image_bytes = handle.read()

    encoded = model.encode_image(image_bytes)
    result = model.answer_question(encoded, prompt)

    if isinstance(result, dict):
        result = result.get("answer", "")
    if not isinstance(result, str):
        result = str(result)
    return result


def main():
    _silence_stdout()
    _emit({"status": "loading"})

    try:
        model = _load_model()
    except Exception as exc:
        _emit({"status": "error", "message": str(exc)})
        return 1

    _emit({"status": "ready"})

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue

        try:
            request = json.loads(line)
            answer = _answer(model, request["image"], request["prompt"])
            _emit({"answer": answer})
        except Exception as exc:
            # A single bad request must not kill the worker; keep serving.
            _emit({"error": "%s" % exc})

    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        sys.exit(0)
    except Exception:
        traceback.print_exc(file=sys.stderr)
        try:
            _emit({"error": "worker crashed: %s" % sys.exc_info()[1]})
        except Exception:
            pass
        sys.exit(1)
