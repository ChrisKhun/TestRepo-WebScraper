import os
import requests
from flask import Flask, jsonify
from dotenv import load_dotenv


# ==================================================
#           LOAD ENVIRONMENT VARIABLES
# ==================================================

print("Loading environment variables...")
load_dotenv()

UNIPILE_DSN = os.getenv("UNIPILE_DSN", "").rstrip("/")
UNIPILE_API_KEY = os.getenv("UNIPILE_API_KEY", "")

print("UNIPILE_DSN loaded:", bool(UNIPILE_DSN))
print("UNIPILE_API_KEY loaded:", bool(UNIPILE_API_KEY))

if not UNIPILE_DSN or not UNIPILE_API_KEY:
    raise RuntimeError("Missing UNIPILE_DSN or UNIPILE_API_KEY in .env")


# ==================================================
#                 FLASK APP SETUP
# ==================================================

app = Flask(__name__)


# ==================================================
#                     HELPERS
# ==================================================

def ping_unipile():
    """
    Simple connectivity test to Unipile.
    Calls GET /api/v1/accounts
    """
    url = f"{UNIPILE_DSN}/api/v1/accounts"
    headers = {
        "X-API-KEY": UNIPILE_API_KEY,
        "accept": "application/json",
    }

    response = requests.get(url, headers=headers, timeout=15)
    return response


# ==================================================
#                     ROUTES
# ==================================================

@app.get("/")
def home():
    """
    Basic sanity endpoint.
    Confirms Flask is running.
    """
    return jsonify({
        "ok": True,
        "message": "Flask server is running",
        "next_step": "/health/unipile"
    })


@app.get("/health/unipile")
def health_unipile():
    """
    Browser-accessible Unipile health check.
    """
    try:
        r = ping_unipile()
        return jsonify({
            "ok": r.ok,
            "status_code": r.status_code,
            "response": r.text
        }), (200 if r.ok else r.status_code)

    except Exception as e:
        return jsonify({
            "ok": False,
            "error": str(e)
        }), 500


# ==================================================
#                 APPLICATION START
# ==================================================

if __name__ == "__main__":
    print("Pinging Unipile on startup...")
    try:
        r = ping_unipile()
        print("Unipile status code:", r.status_code)
        print("Unipile response:", r.text)
    except Exception as e:
        print("Unipile ping FAILED:", repr(e))

    print("Starting Flask server at http://localhost:5000")
    app.run(port=5000, debug=True)