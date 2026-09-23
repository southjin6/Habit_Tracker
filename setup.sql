-- One-time provisioning for the Habit Tracker database.
--
-- Run this as a MySQL account that can create databases and users, e.g.:
--     mysql -u <admin> -p < setup.sql
--
-- Step 1: replace CHANGE_ME below with the local password you want for the app account, run the
-- script, then put CHANGE_ME back and use that same password in .env (HABIT_CONNECTION). The point
-- of the round trip is that setup.sql is a shared, committed file while .env is gitignored: the
-- real value should only ever be typed into the script, never left in it. If you run this script
-- unchanged, the app simply fails to connect until the two values match — no silent fallback.
--
-- If the user already exists, CREATE USER IF NOT EXISTS is a no-op and the password is unchanged.
-- To rotate it afterwards:
--     ALTER USER 'habit_user'@'localhost' IDENTIFIED BY '<new password>';

CREATE DATABASE IF NOT EXISTS habit_tracker
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

CREATE USER IF NOT EXISTS 'habit_user'@'localhost' IDENTIFIED BY 'CHANGE_ME';

GRANT ALL PRIVILEGES ON habit_tracker.* TO 'habit_user'@'localhost';

FLUSH PRIVILEGES;
