using RabbitMQ.Client.Events;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using TodoApp.Shared.Models;
using TodoApp.WorkerService.Helpers;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

   public static string CreateSuccessResponse()
   {
      return JsonSerializer.Serialize(new RpcResponse { 
         Success = true,
         CreatedId = null,
         Error = null
      });
   }

   public static string CreateSuccessResponse(int createdId)
   {
      return JsonSerializer.Serialize(new RpcResponse { 
         Success = true,
         CreatedId = createdId,
         Error = null
      });
   }

   public static string CreateErrorResponse(Exception ex)
   {
      var kind = ex switch
      {
         KeyNotFoundException => RpcErrorKind.NOT_FOUND,
         InvalidOperationException => RpcErrorKind.VALIDATION,
         DbUpdateException dbEx when dbEx.InnerException is PostgresException pgEx && 
            (pgEx.SqlState == "23505" || pgEx.SqlState == "23503") => RpcErrorKind.VALIDATION,
         _ => RpcErrorKind.FATAL
      };
      
      // For database errors, provide a more user-friendly message
      var message = ex switch
      {
         DbUpdateException dbEx when dbEx.InnerException is PostgresException pgEx => pgEx.MessageText,
         _ => ex.Message
      };
      return JsonSerializer.Serialize(new RpcResponse { 
         Success = false,
         Error = new RpcError { 
            Message = message, 
            Kind = kind.ToString() 
         }
      });
   }
}
