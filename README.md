# LinkedIn Webscraper (Unipile API)

A simple personal sandbox project for experimenting with retrieving LinkedIn profile data using the Unipile API.

This is NOT a traditional HTML scraper. Unipile uses authenticated LinkedIn sessions and returns structured JSON data.

---

## What This Project Does

- Connects a LinkedIn account via Unipile
- Stores the returned account_id
- Fetches LinkedIn profile data by identifier
- Lets you safely experiment without DOM scraping

---

## Prerequisites

- Unipile account
- Unipile DSN (API base URL)
- Unipile Access Token (API key)
- A LinkedIn account (use a test account if possible)

---

## Setup

### 1. Create a Unipile Access Token

- Log into the Unipile dashboard
- Generate a new Access Token
- Treat it like an API key (keep it secret)

---

### 2. Environment Variables

Create a `.env` file (do NOT commit it):

UNIPILE_DSN=https://your-dsn.unipile.com:port  
UNIPILE_API_KEY=your_access_token_here  

Recommended:
- Add `.env` to `.gitignore`
- Commit a `.env.example` instead

---

## Connect a LinkedIn Account

Use Unipile’s Hosted Auth Wizard.

Flow:
1. Generate a hosted auth link from your backend
2. Open the link in a browser
3. Log into LinkedIn
4. Receive an account_id via your callback (notify_url)
5. Save the account_id for API calls

---

## Fetch a LinkedIn Profile

You need:
- ACCOUNT_ID (from the auth step)
- IDENTIFIER (LinkedIn public identifier)

Request format:

GET {UNIPILE_DSN}/api/v1/users/{IDENTIFIER}?linkedin_sections=*&account_id={ACCOUNT_ID}

Headers:
- X-API-KEY: your access token
- Accept: application/json

Example values:
- IDENTIFIER: john-doe-12345
- ACCOUNT_ID: returned from Unipile

---

## Suggested Project Structure

linkedin-webscraper/
- src/
  - config/        (env + Unipile client)
  - auth/          (hosted auth + callback)
  - profiles/      (profile fetch logic)
- .env.example
- .gitignore
- README.md

---

## Tips

- One Unipile token per project
- Never hardcode secrets
- Expect rate limits
- Some profiles may return partial data
