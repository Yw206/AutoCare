using System.Security.Claims;
using System.Security.Cryptography;
using AutoCare.Data;
using AutoCare.Models;
using AutoCare.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoCare.Controllers;

public class AccountController(AppDbContext db, EmailService emailService, IWebHostEnvironment environment) : Controller
{
    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View(new LoginVm());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginVm vm, string? returnUrl = null)
    {
        if (!ModelState.IsValid) return View(vm);
        string normalizedEmail = vm.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == normalizedEmail);

        if (user?.LockoutEnd > DateTime.Now)
        {
            int minutes = Math.Max(1, (int)Math.Ceiling((user.LockoutEnd.Value - DateTime.Now).TotalMinutes));
            ModelState.AddModelError("", $"Account temporarily locked. Try again in {minutes} minute(s).");
            return View(vm);
        }

        if (user == null || !PasswordService.Verify(vm.Password, user.PasswordHash))
        {
            if (user != null)
            {
                user.FailedLoginAttempts++;
                if (user.FailedLoginAttempts >= 3)
                {
                    user.LockoutEnd = DateTime.Now.AddMinutes(15);
                    user.FailedLoginAttempts = 0;
                }
                await db.SaveChangesAsync();
            }
            ModelState.AddModelError("", "Invalid email or password.");
            return View(vm);
        }

        if (!user.IsActive)
        {
            ModelState.AddModelError("", "This account is blocked. Please contact the workshop Admin.");
            return View(vm);
        }

        if (!user.IsEmailVerified)
        {
            TempData["Error"] = "Please verify your email before logging in.";
            return RedirectToAction(nameof(VerifyEmail), new { email = user.Email });
        }

        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        await db.SaveChangesAsync();

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role)
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
        return LocalRedirect(string.IsNullOrWhiteSpace(returnUrl) ? (user.Role == "Admin" ? "/Admin" : "/Dashboard") : returnUrl);
    }

    public IActionResult Register() => View(new RegisterVm());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterVm vm)
    {
        string normalizedEmail = vm.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(x => x.Email == normalizedEmail))
            ModelState.AddModelError(nameof(vm.Email), "Email is already registered.");
        if (!ModelState.IsValid) return View(vm);

        string otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var user = new AppUser
        {
            FullName = vm.FullName,
            Email = normalizedEmail,
            Phone = vm.Phone,
            PasswordHash = PasswordService.Hash(vm.Password),
            EmailVerificationOtpHash = PasswordService.Hash(otp),
            EmailVerificationExpiresAt = DateTime.Now.AddMinutes(10)
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        bool sent = await emailService.SendVerificationOtpAsync(user.Email, user.FullName, otp);
        if (!sent && environment.IsDevelopment()) TempData["DevelopmentOtp"] = otp;
        TempData["Success"] = sent
            ? "Registration successful. Check your email for the verification code."
            : "Registration successful. SMTP is not configured, so the development verification code is shown below.";
        return RedirectToAction(nameof(VerifyEmail), new { email = user.Email });
    }

    public IActionResult VerifyEmail(string email) => View(new VerifyEmailVm { Email = email });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyEmail(VerifyEmailVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == vm.Email.Trim().ToLower());
        if (user == null) return NotFound();
        if (user.IsEmailVerified)
        {
            TempData["Success"] = "Email already verified. You can log in.";
            return RedirectToAction(nameof(Login));
        }
        if (user.EmailVerificationExpiresAt < DateTime.Now || user.EmailVerificationOtpHash == null
            || !PasswordService.Verify(vm.Otp, user.EmailVerificationOtpHash))
        {
            ModelState.AddModelError(nameof(vm.Otp), "Invalid or expired verification code.");
            return View(vm);
        }
        user.IsEmailVerified = true;
        user.EmailVerificationOtpHash = null;
        user.EmailVerificationExpiresAt = null;
        await db.SaveChangesAsync();
        TempData["Success"] = "Email verified successfully. Please log in.";
        return RedirectToAction(nameof(Login));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendVerification(string email)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email.Trim().ToLower());
        if (user == null || user.IsEmailVerified) return RedirectToAction(nameof(Login));
        string otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        user.EmailVerificationOtpHash = PasswordService.Hash(otp);
        user.EmailVerificationExpiresAt = DateTime.Now.AddMinutes(10);
        await db.SaveChangesAsync();
        bool sent = await emailService.SendVerificationOtpAsync(user.Email, user.FullName, otp);
        if (!sent && environment.IsDevelopment()) TempData["DevelopmentOtp"] = otp;
        TempData["Success"] = "A new verification code was generated.";
        return RedirectToAction(nameof(VerifyEmail), new { email = user.Email });
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    public IActionResult AccessDenied() => View();
}
