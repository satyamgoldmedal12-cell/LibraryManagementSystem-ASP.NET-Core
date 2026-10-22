using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LibraryManagement.Web.Controllers;

public sealed class AccountController(IAccountService accounts, IMemberRegistrationService registration,
    IPasswordRecoveryService recovery, IUserSessionService sessions, ILogger<AccountController> logger) : LibraryController
{
    private bool IsSignedIn => User.Identity?.IsAuthenticated == true;
    private IActionResult RedirectHome() => RedirectToAction("Index", "Home");

    [HttpGet, AllowAnonymous]
    public IActionResult Register() => IsSignedIn
        ? RedirectHome()
        : View(new RegisterModel());

    [HttpPost, AllowAnonymous, EnableRateLimiting("registration")]
    public async Task<IActionResult> Register(RegisterModel model)
    {
        if (IsSignedIn)
        {
            return RedirectHome();
        }

        if (!ModelState.IsValid) return View(model);

        var result = await registration.RegisterAsync(model);

        if (result.Succeeded)
        {
            logger.LogInformation("Member registration completed");
            Success("Registration successful. Sign in with your email and password.");
            return RedirectToAction(nameof(Login));
        }

        foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
        return View(model);
    }

    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl = null) => LoginView(null, returnUrl);
    [HttpGet, AllowAnonymous]
    public IActionResult AdminLogin(string? returnUrl = null) => LoginView(Roles.Admin, returnUrl);
    [HttpGet, AllowAnonymous]
    public IActionResult LibrarianLogin(string? returnUrl = null) => LoginView(Roles.Librarian, returnUrl);
    [HttpGet, AllowAnonymous]
    public IActionResult MemberLogin(string? returnUrl = null) => LoginView(Roles.Member, returnUrl);

    private IActionResult LoginView(string? portal, string? returnUrl) => IsSignedIn
        ? RedirectHome()
        : View("Login", new LoginModel
        {
            Portal = portal,
            ReturnUrl = returnUrl
        });

    [HttpPost, AllowAnonymous, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginModel model)
    {
        if (IsSignedIn)
        {
            return RedirectHome();
        }

        if (!ModelState.IsValid) return View(model);

        var result = await accounts.LoginAsync(model);

        if (result.Succeeded)
        {
            return Url.IsLocalUrl(model.ReturnUrl)
                ? LocalRedirect(model.ReturnUrl!)
                : RedirectHome();
        }

        foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await accounts.SignOutAsync(); // Cookie events revoke this server-side session as well as removing the cookie.
        return RedirectToAction(nameof(Login));
    }

    [HttpGet, AllowAnonymous]
    public IActionResult ForgotPassword() => View(new ForgotPasswordModel());
    [HttpPost, AllowAnonymous, EnableRateLimiting("password-recovery")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

        await recovery.SendResetAsync(model.Email, ct);
        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    [HttpGet, AllowAnonymous]
    public IActionResult ForgotPasswordConfirmation() => View();

    [HttpGet, AllowAnonymous]
    public IActionResult ResetPassword(string? userId, string? code)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 450 || string.IsNullOrWhiteSpace(code) || code.Length > 4096)
        {
            TempData["Error"] = "Request a new password reset link to continue.";
            return RedirectToAction(nameof(ForgotPassword));
        }

        return View(new PublicResetPasswordModel { UserId = userId, Code = code });
    }

    [HttpPost, AllowAnonymous, EnableRateLimiting("password-reset")]
    public async Task<IActionResult> ResetPassword(PublicResetPasswordModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await recovery.ResetAsync(model, ct);

        if (result.Succeeded)
        {
            Success("Password reset. Sign in with your new password.");
            return RedirectToAction(nameof(Login));
        }

        foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
        return View(model);
    }

    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordModel());
    [HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await accounts.ChangePasswordAsync(Actor, model, ct);

        if (result.IsNotFound) return Challenge();

        if (result.Succeeded)
        {
            Success("Password changed. Your other sessions have been signed out.");
            return RedirectHome();
        }

        foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Sessions(CancellationToken ct)
    {
        var current = await accounts.CurrentSessionIdAsync();

        if (current is null) return Challenge();

        return View(new SessionListModel(await sessions.ListAsync(Actor, ct), current.Value));
    }

    [HttpPost]
    public async Task<IActionResult> RevokeSession(Guid id, CancellationToken ct)
    {
        var current = await accounts.CurrentSessionIdAsync();
        await sessions.RevokeAsync(id, Actor, ct); // SQL always includes the signed-in user's ID.

        if (id == current) return await Logout();

        Success("Session signed out.");
        return RedirectToAction(nameof(Sessions));
    }

    [HttpPost]
    public async Task<IActionResult> RevokeOtherSessions(CancellationToken ct)
    {
        var current = await accounts.CurrentSessionIdAsync();

        if (current is null) return Challenge();

        await sessions.RevokeOthersAsync(Actor, current.Value, ct);

        Success("Your other sessions have been signed out.");
        return RedirectToAction(nameof(Sessions));
    }

    [AllowAnonymous]
    public IActionResult AccessDenied() { Response.StatusCode = 403; return View(); }
}
