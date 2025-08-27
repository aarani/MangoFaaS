using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddAuthorization();

builder
    .Services
    .AddIdentityApiEndpoints<IdentityUser>()
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<MangoAuthDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddDbContext<MangoAuthDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("authdb")
        ?? throw new InvalidOperationException("Connection string 'authdb' not found.")));

builder.Services.AddCors(options =>
    {
        options.AddPolicy("MyCors",
            policy =>
            {
                policy
                    .WithOrigins("http://localhost")
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
    });

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var dbContext = services.GetRequiredService<MangoAuthDbContext>();
    await dbContext.Database.MigrateAsync();

#if DEBUG
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    await roleManager.CreateAsync(new IdentityRole("Admin"));

    var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
    var user = new IdentityUser();
    await userManager.SetUserNameAsync(user, "afshin@arani.dev");
    await userManager.SetEmailAsync(user, "afshin@arani.dev");
    _ = await userManager.CreateAsync(user, "123456Aa!@#");
    await userManager.AddToRolesAsync(user, ["Admin"]);
#endif
}

app.UseHttpsRedirection();
app.UseCors("MyCors");
app.MapIdentityApi<IdentityUser>();
await app.RunAsync();