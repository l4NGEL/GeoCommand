using GeoCommand.Application.Abstractions;
using GeoCommand.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace GeoCommand.Api.Hosting;

/// <summary>Domain ve uygulama hatalarını anlaşılır Türkçe ProblemDetails yanıtlarına çevirir.</summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            DomainValidationException ex => Validation(ex.Errors),
            DomainRuleException ex => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "İşlem mevcut durumda yapılamıyor.",
                Detail = ex.Message
            },
            NotFoundException ex => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Kayıt bulunamadı.",
                Detail = ex.Message
            },
            BadHttpRequestException ex => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "İstek okunamadı.",
                Detail = "İstek gövdesi veya parametreleri beklenen biçimde değil. " + (ex.InnerException?.Message ?? ex.Message)
            },
            _ => null
        };

        if (problem is null)
        {
            logger.LogError(exception, "Beklenmeyen hata. Yol={Path}", context.Request.Path);
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Beklenmeyen bir sunucu hatası oluştu.",
                Detail = "Ayrıntılar sunucu loglarında traceId ile aranabilir."
            };
        }
        else
        {
            logger.LogInformation("İstek reddedildi. Yol={Path} Durum={Status} Neden={Detail}",
                context.Request.Path, problem.Status, problem.Detail ?? problem.Title);
        }

        context.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    public static ProblemDetails Validation(IReadOnlyList<string> errors) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "Girdi doğrulanamadı.",
        Detail = string.Join(" ", errors),
        Extensions = { ["errors"] = errors }
    };
}
