﻿using Microsoft.Data.Sqlite;

namespace LiveCaptionsTranslator.models
{
    public class TranslationHistoryEntry
    {
        public DateTime Timestamp { get; set; }
        public string SourceText { get; set; }
        public string TranslatedText { get; set; }
        public string TargetLanguage { get; set; }
        public string ApiUsed { get; set; }
    }

    public static class SQLiteHistoryLogger
    {
        private static readonly string ConnectionString = "Data Source=translation_history.db;";

        static SQLiteHistoryLogger()
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                string createTableQuery = @"
                    CREATE TABLE IF NOT EXISTS TranslationHistory (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Timestamp TEXT,
                        SourceText TEXT,
                        TranslatedText TEXT,
                        TargetLanguage TEXT,
                        ApiUsed TEXT
                    )";
                using (var command = new SqliteCommand(createTableQuery, connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        public static async Task LogTranslation(string sourceText, string translatedText, string targetLanguage,
            string apiUsed)
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                await connection.OpenAsync();

                string selectQuery = @"
                    SELECT SourceText 
                    FROM TranslationHistory 
                    ORDER BY Timestamp DESC 
                    LIMIT 1";

                using (var selectCommand = new SqliteCommand(selectQuery, connection))
                using (var reader = await selectCommand.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        string latestSourceText = reader.GetString(reader.GetOrdinal("SourceText"));
                        if (latestSourceText == sourceText)
                        {
                            // If latest SourceText is the same as the one to be inserted, give up to prevent duplicate
                            return;
                        }
                    }
                }

                string insertQuery = @"
                    INSERT INTO TranslationHistory (Timestamp, SourceText, TranslatedText, TargetLanguage, ApiUsed)
                    VALUES (@Timestamp, @SourceText, @TranslatedText, @TargetLanguage, @ApiUsed)";

                using (var command = new SqliteCommand(insertQuery, connection))
                {
                    command.Parameters.AddWithValue("@Timestamp", DateTime.Now);
                    command.Parameters.AddWithValue("@SourceText", sourceText);
                    command.Parameters.AddWithValue("@TranslatedText", translatedText);
                    command.Parameters.AddWithValue("@TargetLanguage", targetLanguage);
                    command.Parameters.AddWithValue("@ApiUsed", apiUsed);
                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        public static async Task<List<TranslationHistoryEntry>> LoadHistory()
        {
            var history = new List<TranslationHistoryEntry>();

            using (var connection = new SqliteConnection(ConnectionString))
            {
                await connection.OpenAsync();
                string selectQuery = @"
                    SELECT Timestamp, SourceText, TranslatedText, TargetLanguage, ApiUsed 
                    FROM TranslationHistory ORDER BY Timestamp DESC";

                using (var command = new SqliteCommand(selectQuery, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        history.Add(new TranslationHistoryEntry
                        {
                            Timestamp = reader.GetDateTime(reader.GetOrdinal("Timestamp")),
                            SourceText = reader.GetString(reader.GetOrdinal("SourceText")),
                            TranslatedText = reader.GetString(reader.GetOrdinal("TranslatedText")),
                            TargetLanguage = reader.GetString(reader.GetOrdinal("TargetLanguage")),
                            ApiUsed = reader.GetString(reader.GetOrdinal("ApiUsed"))
                        });
                    }
                }
            }
            return history;
        }

        public static async Task ClearHistory()
        {
            await Task.Run(() =>
            {
                using var connection = new SqliteConnection(ConnectionString);
                connection.Open();
                using var command = new SqliteCommand("DELETE FROM TranslationHistory", connection);
                command.ExecuteNonQuery();
            });
        }
    }
}