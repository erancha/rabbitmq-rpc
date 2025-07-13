using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TodoApp.Shared.Messages;
using TodoApp.WorkerService.Helpers;
using static TodoApp.Shared.Messages.RpcErrorKind;

namespace TodoApp.WorkerService.Helpers;

public static class RpcResponseHelper
{
    public static void SendRpcResponse(IModel channel, BasicDeliverEventArgs ea, string rpcResponse)
    {
        if (!string.IsNullOrEmpty(ea.BasicProperties.ReplyTo))
        {
            var replyProps = channel.CreateBasicProperties();
            replyProps.CorrelationId = ea.BasicProperties.CorrelationId;
            var responseBytes = Encoding.UTF8.GetBytes(rpcResponse ?? "null");
            channel.BasicPublish("", ea.BasicProperties.ReplyTo, replyProps, responseBytes);
        }
    }

    private static ILogger? _logger;

    public static void SetLogger(ILogger logger)
    {
        _logger = logger;
    }

    public static string CreateSuccessResponse()
    {
        var response = new RpcResponse
        {
            Success = true,
            CreatedId = null,
            Error = null,
        };
        var json = JsonSerializer.Serialize(response);
        _logger?.LogInformation("CreateSuccessResponse() -> {Response}", json);
        return json;
    }

    public static string CreateSuccessResponse(int createdId)
    {
        var response = new RpcResponse
        {
            Success = true,
            CreatedId = createdId,
            Error = null,
        };
        var json = JsonSerializer.Serialize(response);
        _logger?.LogInformation("CreateSuccessResponse({CreatedId}) -> {Response}", createdId, json);
        return json;
    }

    public static string CreateSuccessResponse<T>(T data)
    {
        var response = new RpcResponse<T> { Success = true, Data = data };
        var json = JsonSerializer.Serialize(response);
        _logger?.LogInformation("CreateSuccessResponse<{Type}>({Data}) -> {Response}", typeof(T).Name, data, json);
        return json;
    }

    public static string CreateErrorResponse<T>(string message, string kind = "UNKNOWN")
    {
        var response = new RpcResponse<T>
        {
            Success = false,
            Error = new RpcError { Message = message, Kind = kind },
        };
        var json = JsonSerializer.Serialize(response);
        _logger?.LogInformation("CreateErrorResponse<{Type}>({Message}, {Kind}) -> {Response}", typeof(T).Name, message, kind, json);
        return json;
    }

    public static string CreateErrorResponse(Exception ex)
    {
        var kind = ex switch
        {
            KeyNotFoundException => RpcErrorKind.NOT_FOUND.ToString(),
            InvalidOperationException => RpcErrorKind.VALIDATION.ToString(),
            DbUpdateException dbEx
                when dbEx.InnerException is PostgresException pgEx
                    && (pgEx.SqlState == "23505" || pgEx.SqlState == "23503") => RpcErrorKind.VALIDATION.ToString(),
            _ => RpcErrorKind.FATAL.ToString()
        };

        // For database errors, provide a more user-friendly message
        var message = ex switch
        {
            DbUpdateException dbEx when dbEx.InnerException is PostgresException pgEx =>
                pgEx.MessageText,
            _ => ex.Message,
        };

        var response = new RpcResponse
        {
            Success = false,
            Error = new RpcError { Message = message, Kind = kind },
        };
        var json = JsonSerializer.Serialize(response);
        _logger?.LogInformation("CreateErrorResponse({ExType}, {Message}, {Kind}) -> {Response}", ex.GetType().Name, message, kind, json);
        return json;
    }
}
