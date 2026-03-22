using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using SocialMediaBot.Features.Gemini;
using System.Security.Claims;

namespace SocialMediaBot.Features.Admin;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/admin");

        group.MapGet("/", (HttpContext ctx) =>
            ctx.User.Identity?.IsAuthenticated == true
                ? Results.Redirect("/admin/panel")
                : Results.Redirect("/admin/login"))
            .AllowAnonymous();

        group.MapGet("/login", (IWebHostEnvironment env) =>
            ServeHtml(env, "login.html"))
            .AllowAnonymous();

        group.MapPost("/login", async (HttpContext ctx, IOptions<AdminOptions> opts) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var user = form["username"].ToString();
            var pass = form["password"].ToString();
            var o = opts.Value;

            if (string.IsNullOrEmpty(o.Password) || user != o.Username || pass != o.Password)
                return Results.Redirect("/admin/login?error=1");

            var claims = new[] { new Claim(ClaimTypes.Name, user) };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity));

            return Results.Redirect("/admin/panel");
        }).AllowAnonymous();

        var secured = group.RequireAuthorization();

        secured.MapGet("/panel", (IWebHostEnvironment env) =>
            ServeHtml(env, "panel.html"));

        secured.MapGet("/api/status", (AppState state) =>
            Results.Ok(new
            {
                isDryRun = state.IsDryRun,
                isBotRunning = state.IsBotRunning
            }));

        secured.MapPost("/dryrun/toggle", (AppState state) =>
        {
            state.IsDryRun = !state.IsDryRun;
            return Results.Ok(new { success = true, isDryRun = state.IsDryRun });
        });

        secured.MapPost("/bot/toggle", (AppState state) =>
        {
            state.ToggleBot();
            return Results.Ok(new { success = true, isBotRunning = state.IsBotRunning });
        });

        secured.MapPost("/prompts/reload", (IGeminiService gemini) =>
        {
            gemini.InvalidatePromptCache();
            return Results.Ok(new { success = true });
        });

        secured.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/admin/login");
        });

        return routes;
    }

    private static IResult ServeHtml(IWebHostEnvironment env, string fileName)
    {
        var path = Path.Combine(env.WebRootPath, "admin", fileName);
        if (!File.Exists(path))
            return Results.NotFound();

        var html = File.ReadAllText(path);
        return Results.Content(html, "text/html; charset=utf-8");
    }
}
