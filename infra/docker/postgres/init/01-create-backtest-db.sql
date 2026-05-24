-- Create the backtest database (marketdata DB is created by POSTGRES_DB env var).
SELECT 'CREATE DATABASE signalstack_backtest'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'signalstack_backtest')\gexec
