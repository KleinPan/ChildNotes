using ChildNotes.Core.Common;
using ChildNotes.Core.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ChildNotes.Api.Filters;

/// <summary>
/// 统一包装 Controller 返回值为 ApiResponse<T>
/// </summary>
public class ApiResponseWrapperFilter : IActionFilter, IOrderedFilter
{
    public int Order => int.MaxValue - 10;

    public void OnActionExecuting(ActionExecutingContext context) { }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Exception is BusinessException ex)
        {
            context.Result = new ObjectResult(ApiResponse.Fail(ex.Message, ex.ErrorCode))
            {
                StatusCode = ex.StatusCode >= 400 && ex.StatusCode < 600 ? ex.StatusCode : 400,
            };
            context.ExceptionHandled = true;
            return;
        }

        if (context.Exception is not null) return;

        switch (context.Result)
        {
            case ObjectResult obj when obj.Value is not null && obj.Value.GetType().IsGenericType
                && obj.Value.GetType().GetGenericTypeDefinition() == typeof(ApiResponse<>):
                return; // 已是 ApiResponse，跳过
            case ObjectResult obj:
                context.Result = new ObjectResult(ApiResponse.Ok(obj.Value)) { StatusCode = obj.StatusCode };
                break;
            case EmptyResult:
                context.Result = new ObjectResult(ApiResponse.Ok()) { StatusCode = 200 };
                break;
        }
    }
}
