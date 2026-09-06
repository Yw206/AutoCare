using System.Security.Claims;
using System.Security.Cryptography;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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
    private const string LoginCaptchaKey = "LoginCaptchaAnswer";
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View(new LoginVm());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginVm vm, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View(vm);
        }
        string normalizedEmail = vm.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == normalizedEmail);
        DateTime? lockoutEnd = user?.LockoutEnd;
        bool lockoutActive = lockoutEnd.HasValue && lockoutEnd.Value > DateTime.Now;
        bool captchaRequired = lockoutEnd.HasValue && lockoutEnd.Value <= DateTime.Now;

        if (lockoutActive)
        {
            int minutes = Math.Max(1, (int)Math.Ceiling((lockoutEnd!.Value - DateTime.Now).TotalMinutes));
            ModelState.AddModelError("", $"Account temporarily locked. Try again in {minutes} minute(s).");
            ViewBag.RequireCaptcha = true;
            PrepareCaptcha(LoginCaptchaKey);
            ViewBag.ReturnUrl = returnUrl;
            return View(vm);
        }

        if (captchaRequired && !CaptchaIsValid(LoginCaptchaKey, vm.CaptchaAnswer))
        {
            ModelState.AddModelError(nameof(vm.CaptchaAnswer), "Incorrect security answer.");
            ViewBag.RequireCaptcha = true;
            PrepareCaptcha(LoginCaptchaKey);
            ViewBag.ReturnUrl = returnUrl;
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
                    ViewBag.RequireCaptcha = true;
                }
                await db.SaveChangesAsync();
            }
            ModelState.AddModelError("", user?.LockoutEnd > DateTime.Now
                ? "Too many failed attempts. Account is temporarily locked and captcha will be required after the lock expires."
                : "Invalid email or password.");
            if (ViewBag.RequireCaptcha == true) PrepareCaptcha(LoginCaptchaKey);
            ViewBag.ReturnUrl = returnUrl;
            return View(vm);
        }

        if (!user.IsActive)
        {
            ModelState.AddModelError("", "This account is blocked. Please contact the workshop Admin.");
            ViewBag.ReturnUrl = returnUrl;
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

    public IActionResult Register()
    {
        return View(new RegisterVm());
    }

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

    public IActionResult ForgotPassword() => View(new ForgotPasswordVm());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        string email = vm.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email && x.IsActive);
        if (user != null)
        {
            string otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            user.PasswordResetOtpHash = PasswordService.Hash(otp);
            user.PasswordResetExpiresAt = DateTime.Now.AddMinutes(10);
            await db.SaveChangesAsync();
            await emailService.SendPasswordResetOtpAsync(user.Email, user.FullName, otp);
            TempData["Success"] = "If the email exists, a password reset code was sent.";
        }
        else
        {
            TempData["Success"] = "If the email exists, a password reset code was sent.";
        }
        return RedirectToAction(nameof(ResetPassword), new { email });
    }

    public IActionResult ResetPassword(string email) => View(new ResetPasswordVm { Email = email });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == vm.Email.Trim().ToLowerInvariant());
        if (user == null || user.PasswordResetExpiresAt < DateTime.Now || user.PasswordResetOtpHash == null
            || !PasswordService.Verify(vm.Otp, user.PasswordResetOtpHash))
        {
            ModelState.AddModelError(nameof(vm.Otp), "Invalid or expired reset code.");
            return View(vm);
        }
        user.PasswordHash = PasswordService.Hash(vm.NewPassword);
        user.PasswordResetOtpHash = null;
        user.PasswordResetExpiresAt = null;
        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        await db.SaveChangesAsync();
        TempData["Success"] = "Password reset successfully. Please sign in.";
        return RedirectToAction(nameof(Login));
    }

    [Authorize]
    public async Task<IActionResult> Profile()
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user == null) return NotFound();
        return View(new ProfileVm
        {
            FullName = user.FullName,
            Phone = user.Phone ?? "",
            ThemePreference = user.ThemePreference,
            LanguagePreference = user.LanguagePreference,
            ProfilePhotoPath = user.ProfilePhotoPath
        });
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> Profile(ProfileVm vm, IFormFile? profilePhoto, string? croppedProfilePhoto)
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user == null) return NotFound();
        if (!ModelState.IsValid)
        {
            vm.ProfilePhotoPath = user.ProfilePhotoPath;
            return View(vm);
        }

        user.FullName = vm.FullName.Trim();
        user.Phone = vm.Phone.Trim();
        user.ThemePreference = vm.ThemePreference == "dark" ? "dark" : "light";
        user.LanguagePreference = NormalizeLanguage(vm.LanguagePreference);
        Response.Cookies.Append("AutoCare.Language", user.LanguagePreference, new CookieOptions { Expires = DateTimeOffset.Now.AddYears(1), IsEssential = true });

        if (!string.IsNullOrWhiteSpace(croppedProfilePhoto))
        {
            string folder = Path.Combine(environment.WebRootPath, "uploads", "profiles");
            Directory.CreateDirectory(folder);
            string fileName = $"user-{user.Id}-{Guid.NewGuid():N}.jpg";
            try
            {
                byte[] imageBytes = DecodeProfileImage(croppedProfilePhoto);
#pragma warning disable CA1416
                await using var upload = new MemoryStream(imageBytes);
                using var source = Image.FromStream(upload);
                using var avatar = CropSquare(source, 256);
                avatar.Save(Path.Combine(folder, fileName), ImageFormat.Jpeg);
#pragma warning restore CA1416
            }
            catch
            {
                ModelState.AddModelError("", "Profile photo could not be processed. Please choose a valid image file.");
                vm.ProfilePhotoPath = user.ProfilePhotoPath;
                return View(vm);
            }
            user.ProfilePhotoPath = "/uploads/profiles/" + fileName;
        }
        else if (profilePhoto is { Length: > 0 })
        {
            string extension = Path.GetExtension(profilePhoto.FileName).ToLowerInvariant();
            if (!new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(extension) || profilePhoto.Length > 5_000_000)
            {
                ModelState.AddModelError("", "Profile photo must be JPG, PNG or WebP and under 5 MB.");
                vm.ProfilePhotoPath = user.ProfilePhotoPath;
                return View(vm);
            }
            string folder = Path.Combine(environment.WebRootPath, "uploads", "profiles");
            Directory.CreateDirectory(folder);
            string fileName = $"user-{user.Id}-{Guid.NewGuid():N}.jpg";
            try
            {
#pragma warning disable CA1416
                await using var upload = profilePhoto.OpenReadStream();
                using var source = Image.FromStream(upload);
                using var avatar = CropSquare(source, 256);
                avatar.Save(Path.Combine(folder, fileName), ImageFormat.Jpeg);
#pragma warning restore CA1416
            }
            catch
            {
                ModelState.AddModelError("", "Profile photo could not be processed. Please choose a valid image file.");
                vm.ProfilePhotoPath = user.ProfilePhotoPath;
                return View(vm);
            }
            user.ProfilePhotoPath = "/uploads/profiles/" + fileName;
        }

        await db.SaveChangesAsync();
        await RefreshSignInAsync(user);
        TempData["Success"] = "Profile updated.";
        return RedirectToAction(nameof(Profile));
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetLanguage(string language, string? returnUrl = null)
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user != null)
        {
            user.LanguagePreference = NormalizeLanguage(language);
            await db.SaveChangesAsync();
        }
        Response.Cookies.Append("AutoCare.Language", NormalizeLanguage(language), new CookieOptions { Expires = DateTimeOffset.Now.AddYears(1), IsEssential = true });
        return LocalRedirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl);
    }

    [Authorize]
    public IActionResult ChangePassword() => View(new ChangePasswordVm());

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user == null) return NotFound();
        if (!PasswordService.Verify(vm.CurrentPassword, user.PasswordHash))
        {
            ModelState.AddModelError(nameof(vm.CurrentPassword), "Current password is incorrect.");
            return View(vm);
        }
        user.PasswordHash = PasswordService.Hash(vm.NewPassword);
        await db.SaveChangesAsync();
        TempData["Success"] = "Password changed successfully.";
        return RedirectToAction(nameof(Profile));
    }

    private async Task RefreshSignInAsync(AppUser user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role)
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
    }

    private void PrepareCaptcha(string key)
    {
        int left = RandomNumberGenerator.GetInt32(2, 10);
        int right = RandomNumberGenerator.GetInt32(2, 10);
        HttpContext.Session.SetInt32(key, left + right);
        ViewBag.CaptchaQuestion = $"{left} + {right} = ?";
    }

    private bool CaptchaIsValid(string key, string answer)
    {
        int? expected = HttpContext.Session.GetInt32(key);
        return expected.HasValue && int.TryParse(answer, out int actual) && actual == expected.Value;
    }

    private static string NormalizeLanguage(string? language) =>
        language is "zh" or "ms" ? language : "en";

    private static byte[] DecodeProfileImage(string dataUrl)
    {
        const string prefix = "data:image/jpeg;base64,";
        if (!dataUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsupported profile photo data.");

        return Convert.FromBase64String(dataUrl[prefix.Length..]);
    }

#pragma warning disable CA1416
    private static Bitmap CropSquare(Image source, int size)
    {
        int side = Math.Min(source.Width, source.Height);
        var crop = new Rectangle((source.Width - side) / 2, (source.Height - side) / 2, side, side);
        var target = new Bitmap(size, size);
        target.SetResolution(source.HorizontalResolution, source.VerticalResolution);
        using var graphics = Graphics.FromImage(target);
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, size, size), crop, GraphicsUnit.Pixel);
        return target;
    }
#pragma warning restore CA1416

    public IActionResult AccessDenied() => View();
}
