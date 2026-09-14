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
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

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
// 开发/测试环境若未配置则使用固定 dev 密钥（重启不变，避免客户端 Token 全量失效）
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
{
    if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
    {
        // 开发/测试环境：固定密钥（不随重启变化）。若每次重启随机生成，服务端所有已签发
        // AccessToken/RefreshToken 会全量失效，客户端被迫重新登录，并放大客户端并发 Refresh 竞态。
        // 可通过 user-secrets 覆盖：dotnet user-secrets set "Jwt:Secret" "<>=32字符>"（优先级高于此默认值）
        jwtSecret = "dev-only-fixed-secret-childnotes-local-9f2a1c4e";
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
        // 从配置读取 issuer/audience，与 JwtTokenService 签发时使用的值保持一致
        var issuer = builder.Configuration["Jwt:Issuer"] ?? "childnotes";
        var audience = builder.Configuration["Jwt:Audience"] ?? "childnotes-app";
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            NameClaimType = "uid",
            // ClockSkew: 5 秒时钟偏移容差（服务器 NTP 同步正常时的合理值；默认 30s 对 15 分钟有效期的 token 偏大）
            ClockSkew = TimeSpan.FromSeconds(5),
        };
        // JWT 认证失败（未带 token/过期/无效）时统一写 ApiResponse 格式响应体，
        // 与 BusinessException / RateLimit / AdminAuthMiddleware 的错误信封一致，前端无需兼容多格式
        opt.Events = new JwtBearerEvents
        {
            OnChallenge = async ctx =>
            {
                ctx.HandleResponse();
                if (ctx.Response.HasStarted) return;
                ctx.Response.StatusCode = 401;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                await ctx.Response.WriteAsJsonAsync(ChildNotes.Core.Common.ApiResponse.Fail("未登录或登录已失效"));
            },
            OnForbidden = async ctx =>
            {
                if (ctx.Response.HasStarted) return;
                ctx.Response.StatusCode = 403;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                await ctx.Response.WriteAsJsonAsync(ChildNotes.Core.Common.ApiResponse.Fail("无权访问该资源"));
            },
        };
    });
builder.Services.AddAuthorization();

// 依赖注入
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<IReferrerCodeUtil>(new ReferrerCodeUtil(jwtSecret));
// 密码哈希：Admin 仍需要（Admin 独立认证体系），用户端不再使用
builder.Services.Configure<PasswordHashOptions>(builder.Configuration.GetSection("PasswordHash"));
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
// 邮箱认证：SMTP + 验证码 + RefreshToken
builder.Services.Configure<EmailAuthOptions>(builder.Configuration.GetSection("EmailAuth"));
builder.Services.AddScoped<IEmailSender, MailKitEmailSender>();
// 宝宝访问权限校验：消除 AiAnalysisService/BabyService/RecordService/SyncService 中的重复
builder.Services.AddScoped<IBabyAccessService, BabyAccessService>();
// 家庭解析（"当前家庭"唯一解析点，Family-centric 模型）
builder.Services.AddScoped<IFamilyService, FamilyService>();
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
// AI 积分消耗配置：支持从 appsettings.json 的 Ai:Cost 节点读取，默认 AnalysisCost=10
builder.Services.Configure<ChildNotes.Core.Config.AiCostOptions>(builder.Configuration.GetSection("Ai:Cost"));
builder.Services.AddSingleton<ChildNotes.Core.Config.AiCostOptions>(sp =>
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ChildNotes.Core.Config.AiCostOptions>>().Value);
builder.Services.AddScoped<IAiAnalysisService, AiAnalysisService>();
builder.Services.AddScoped<IAiNoteService, AiNoteService>();
builder.Services.AddScoped<ICurrentAdminService, CurrentAdminService>();
builder.Services.AddScoped<IAdminAuthService, AdminAuthService>();
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();
builder.Services.AddScoped<IAdminLotteryService, AdminLotteryService>();
builder.Services.AddScoped<ISyncService, SyncService>();
builder.Services.AddScoped<IMilestoneService, MilestoneService>();
// 会员服务：套餐查询、订单创建、支付回调、AI 次数管理
builder.Services.Configure<ChildNotes.Core.Config.MembershipOptions>(builder.Configuration.GetSection("Membership"));
builder.Services.AddSingleton<ChildNotes.Core.Config.MembershipOptions>(sp =>
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ChildNotes.Core.Config.MembershipOptions>>().Value);
// 支付宝客户端：Singleton（无状态，密钥配置运行期不变），替代 MembershipService 内 new
builder.Services.AddSingleton<AlipayAppPayClient>(sp =>
    new AlipayAppPayClient(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ChildNotes.Core.Config.MembershipOptions>>().Value.Alipay));
builder.Services.AddScoped<IMembershipService, MembershipService>();

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
    c.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer", null!),
            new List<string>()
        }
    });
});

// CORS：默认放开（原生 App 无 Origin 概念）；生产环境可通过 CORS:AllowedOrigins
// 配置白名单（逗号分隔，如 https://admin.example.com）收紧为已知来源
var allowedOrigins = builder.Configuration.GetSection("CORS:AllowedOrigins").Get<string[]>();
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
{
    if (allowedOrigins is { Length: > 0 })
        p.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader();
    else
        p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
}));

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

// 可信代理头处理（Caddy 反代同机部署场景）：把 X-Forwarded-For 从右往左解析，
// RemoteIpAddress 回填为第一个"非可信代理"的 IP（即真实客户端 IP）。
// 已知代理仅本机回环（ForwardedHeadersOptions 默认 KnownNetworks 含 127.0.0.0/8 与 ::1/128），
// 客户端伪造的 XFF 首段会被正确跳过——限流中间件据此取 IP，堵住"伪造 XFF 绕过限流"的路径。
// 仅当 RateLimit:TrustProxyHeaders=true（部署在反向代理之后）时启用。
var rateLimitOpt = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ChildNotes.Core.Config.RateLimitOptions>>().Value;
if (rateLimitOpt.TrustProxyHeaders)
{
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    });
}

// 全局异常处理：将未捕获异常统一包装为 ApiResponse，避免泄漏堆栈
app.UseMiddleware<ChildNotes.Infrastructure.Middleware.GlobalExceptionMiddleware>();
app.UseMiddleware<RateLimitMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<AdminAuthMiddleware>();

// 静态文件：默认 wwwroot + 上传目录映射到 /uploads 路径
// 上传文件物理位置在 ContentRootPath/uploads/（不在 wwwroot 下），
// 必须用 PhysicalFileProvider 显式映射，否则 GET /uploads/xxx.jpg 会 404
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.Combine(builder.Environment.ContentRootPath, "uploads")),
    RequestPath = "/uploads"
});

app.MapControllers();

// 健康检查端点：供前端 AI 设置"服务器模式"连通性测试使用。
// 不需要鉴权，仅返回 200 OK。
app.MapGet("/health", () => Results.Ok(new { state = "ok", ts = DateTime.UtcNow }));

app.Run();

// 暴露给 WebApplicationFactory 测试用
public partial class Program { }
