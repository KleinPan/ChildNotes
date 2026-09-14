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

        // [ApiController] 自动模型验证失败（ModelStateInvalidFilter，Order 更小先执行）短路返回
        // ValidationProblemDetails(400)。这是参数校验错误，不能再包装成 {state:"000000"} 成功信封——
        // 前端按 state 判定成败会误判为成功。保持 ProblemDetails 原样返回（HTTP 400 语义由
        // 客户端按状态码处理；泛型包装由 JWT OnChallenge/中间件统一错误格式补充覆盖）。
        if (context.ModelState.IsValid == false) return;

        switch (context.Result)
        {
            case ObjectResult obj when obj.Value is ApiResponseBase:
                return; // 已是 ApiResponse，跳过（基类判断替代反射 GetGenericTypeDefinition）
            case ObjectResult obj:
                context.Result = new ObjectResult(ApiResponse.Ok(obj.Value)) { StatusCode = obj.StatusCode };
                break;
            case EmptyResult:
                context.Result = new ObjectResult(ApiResponse.Ok()) { StatusCode = 200 };
                break;
        }
    }
}
