-- Run once with a deployment account, not the application's SQL account.
IF DB_ID(N'LibraryManagement') IS NULL
    CREATE DATABASE LibraryManagement;
GO
