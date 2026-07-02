using System.Text;
using ChildNotes.Api.Filters;
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
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? "change-this-jwt-secret-before-deploy-at-least-32-chars";

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
// 密码哈希：用户用 Pbkdf2PasswordHasher（单字段格式），Admin 用 AdminPasswordHasher（双字段格式，兼容老数据）
builder.Services.Configure<PasswordHashOptions>(builder.Configuration.GetSection("PasswordHash"));
builder.Services.Configure<AdminPasswordHashOptions>(builder.Configuration.GetSection("AdminPasswordHash"));
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<AdminPasswordHasher>();
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

// 自动迁移（开发期）
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ChildNotesDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseMiddleware<RateLimitMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<AdminAuthMiddleware>();

// 静态文件（本地上传文件访问）
app.UseStaticFiles();

app.MapControllers();

app.Run();

// 暴露给 WebApplicationFactory 测试用
public partial class Program { }
