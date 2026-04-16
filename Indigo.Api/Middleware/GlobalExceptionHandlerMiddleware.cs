using System.Net;
using System.Text.Json;

namespace Indigo.Middleware;

/// <summary>
/// Middleware для глобальной обработки исключений
/// </summary>
public class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

    public GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Логируем ошибку
        _logger.LogError(exception, "Unhandled exception occurred");

        // Определяем статус код на основе типа исключения
        var statusCode = exception switch
        {
            ArgumentException => HttpStatusCode.BadRequest,
            KeyNotFoundException => HttpStatusCode.NotFound,
            UnauthorizedAccessException => HttpStatusCode.Unauthorized,
            InvalidOperationException => HttpStatusCode.BadRequest,
            _ => HttpStatusCode.InternalServerError
        };

        // Создаем объект ответа с ошибкой
        var errorResponse = new
        {
            error = new
            {
                message = GetErrorMessage(exception, statusCode),
                type = exception.GetType().Name,
                timestamp = DateTime.UtcNow
            }
        };

        // Устанавливаем статус код и тип контента
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        // Пишем JSON ответ
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse, jsonOptions));
    }

    private static string GetErrorMessage(Exception exception, HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.BadRequest => "Invalid request data",
            HttpStatusCode.NotFound => "Resource not found",
            HttpStatusCode.Unauthorized => "Unauthorized access",
            HttpStatusCode.InternalServerError => "An internal server error occurred",
            _ => "An error occurred"
        };
    }
}

/// <summary>
/// Extension method для регистрации middleware
/// </summary>
public static class GlobalExceptionHandlerMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<GlobalExceptionHandlerMiddleware>();
    }
}
