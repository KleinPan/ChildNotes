using System.Text;
using System.Text.Json;
using ChildNotes.Api.Filters;
using ChildNotes.Core.Common;
using ChildNotes.Core.Config;
using ChildNotes.Core.Services;
using ChildNotes.Infrastructure.Auth;
using ChildNotes.Infrastructure.Data;
using ChildNotes.Infrastructure.External;
using ChildNotes.Infrastructure.Middleware;
using ChildNotes.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// 配置
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<DeepSeekOptions>(builder.Configuration.GetSection("DeepSeek"));
builder.Services.Configure<OssOptions>(builder.Configuration.GetSection("Oss"));
builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection("RateLimit"));
builder.Services.Configure<UploadOptions>(builder.Configuration.GetSection("Upload"));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection("Admin"));
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? string.Empty;
// 生产环境强制校验 JWT Secret 必须配置且足够长（>=32 字符）
// 开发/测试环境若未配置则生成临时密钥（仅本机使用，重启后失效）
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
{
    if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
    {
        // 开发/测试环境：生成临时密钥，避免本地启动失败
        jwtSecret = "dev-only-secret-" + Guid.NewGuid().ToString("N");
    }
    else
    {
        throw new InvalidOperationException(
            "生产环境必须配置 Jwt:Secret（至少 32 字符）。请在环境变量或 appsettings.Production.json 中设置。");
    }
}
// 将最终使用的 jwtSecret 回写到 JwtOptions，确保 JwtTokenService 与 AddJwtBearer 使用同一密钥
builder.Services.PostConfigure<JwtOptions>(opt => opt.Secret = jwtSecret);

// 数据库
var connStr = builder.Configuration.GetConnectionString("Default")
    ?? "Host=127.0.0.1;Port=5432;Database=child_notes;Username=postgres;Password=;";
if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<ChildNotesDbContext>(opt =>
        opt.UseInMemoryDatabase("test").AddInterceptors(new AuditableSaveChangesInterceptor()));
}
else
{
    builder.Services.AddDbContext<ChildNotesDbContext>(opt =>
        opt.UseNpgsql(connStr)
          .AddInterceptors(new AuditableSaveChangesInterceptor()));
}

// 认证
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            NameClaimType = "uid",
        };
    });
builder.Services.AddAuthorization();

// 依赖注入
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<IReferrerCodeUtil>(new ReferrerCodeUtil(jwtSecret));
// 密码哈希：统一用 Pbkdf2PasswordHasher（单字段格式 iterations:salt:hash）
builder.Services.Configure<PasswordHashOptions>(builder.Configuration.GetSection("PasswordHash"));
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
// 宝宝访问权限校验：消除 AiAnalysisService/BabyService/RecordService/SyncService 中的重复
builder.Services.AddScoped<IBabyAccessService, BabyAccessService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IBabyService, BabyService>();
builder.Services.AddScoped<IRecordService, RecordService>();
builder.Services.AddScoped<IPointsService, PointsService>();
builder.Services.AddScoped<PointsWalletService>();
builder.Services.AddScoped<ISignInService, SignInService>();
builder.Services.AddScoped<ILotteryService, LotteryService>();
builder.Services.AddScoped<IInviteService, InviteService>();
builder.Services.AddScoped<IUploadService, UploadService>();
builder.Services.AddHttpClient<DeepSeekClient>();
builder.Services.AddScoped<IAiAnalysisService, AiAnalysisService>();
builder.Services.AddScoped<IAiNoteService, AiNoteService>();
builder.Services.AddScoped<ICurrentAdminService, CurrentAdminService>();
builder.Services.AddScoped<IAdminAuthService, AdminAuthService>();
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();
builder.Services.AddScoped<IAdminLotteryService, AdminLotteryService>();
builder.Services.AddScoped<ISyncService, SyncService>();
builder.Services.AddScoped<IMilestoneService, MilestoneService>();

// Controllers + 过滤器
builder.Services.AddControllers(opt =>
{
    opt.Filters.Add<ApiResponseWrapperFilter>();
});
// 支持上传大文件
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(opt =>
{
    opt.MultipartBodyLengthLimit = 50 * 1024 * 1024; // 50MB
});
builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(opt =>
{
    opt.Limits.MaxRequestBodySize = 50 * 1024 * 1024;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "ChildNotes API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. 例：Bearer xxxxx",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
            },
            Array.Empty<string>()
        }
    });
});

// CORS
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p => p
    .AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

// 数据库初始化：自动应用未执行的 EF Core Migrations
// InMemory 等非关系型数据库跳过迁移（仅生产 PostgreSQL 环境执行）
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ChildNotesDbContext>();
    if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
    {
        db.Database.Migrate();
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
// 全局异常处理：将未捕获异常统一包装为 ApiResponse，避免泄漏堆栈
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        var logger = context.RequestServices.GetService<ILogger<Program>>();
        logger?.LogError(ex, "未处理异常: {Path}", context.Request.Path);

        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json; charset=utf-8";
        var apiResp = ApiResponse.Fail("服务器内部错误，请稍后重试");
        // 开发/测试环境暴露异常详情，便于排查；生产环境仅返回通用提示
        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
        {
            apiResp = ApiResponse.Fail($"服务器内部错误：{ex.Message}");
        }
        var json = JsonSerializer.Serialize(apiResp, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        await context.Response.WriteAsync(json);
    }
});
app.UseMiddleware<RateLimitMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<AdminAuthMiddleware>();

// 静态文件（本地上传文件访问）
app.UseStaticFiles();

app.MapControllers();

// 健康检查端点：供前端 AI 设置"服务器模式"连通性测试使用。
// 不需要鉴权，仅返回 200 OK。
app.MapGet("/health", () => Results.Ok(new { state = "ok", ts = DateTime.UtcNow }));

app.Run();

// 暴露给 WebApplicationFactory 测试用
public partial class Program { }
