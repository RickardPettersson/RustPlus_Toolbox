// Copyright (c) 2026 Rickard Nordström Pettersson. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// Source: https://github.com/RickardPettersson/RustPlus_FCM

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _filePath;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly Lock _lock = new();

    public FileLoggerProvider(string filePath)
    {
        _filePath = filePath;
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _filePath, _lock));

    public void Dispose() => _loggers.Clear();
}

public sealed class FileLogger(string categoryName, string filePath, Lock fileLock) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel,-12}] [{categoryName}] {formatter(state, exception)}";
        if (exception is not null)
            message += Environment.NewLine + exception;

        lock (fileLock)
        {
            File.AppendAllText(filePath, message + Environment.NewLine);
        }
    }
}

public static class FileLoggerExtensions
{
    public static ILoggingBuilder AddFile(this ILoggingBuilder builder, string filePath)
    {
        builder.AddProvider(new FileLoggerProvider(filePath));
        return builder;
    }
}
