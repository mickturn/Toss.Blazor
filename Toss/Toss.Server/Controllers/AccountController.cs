﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Toss.Shared;
using Toss.Server.Extensions;
using Toss.Server.Models;
using System.Net;
using Toss.Shared.Services;
using System.Security.Claims;
using MediatR;
using System.Collections.Generic;

namespace Toss.Server.Controllers
{
    [Authorize]
    [ApiController]
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IEmailSender _emailSender;
        private readonly ILogger _logger;

        private readonly IMediator _mediator;
        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IEmailSender emailSender,
            ILogger<AccountController> logger,
            IMediator mediator)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _emailSender = emailSender;
            _logger = logger;
            _mediator = mediator;
        }


        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> LoginProviders()
        {
            return Ok(await _mediator.Send(new LoginProvidersQuery()));
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginCommand model)
        {
            var result = await _mediator.Send(model);
            if (result.IsSuccess)
            {
                return Ok();
            }
            if (result.Need2FA)
            {
                return RedirectToAction("/loginWith2fa");
            }
            if (result.IsLockout)
            {
                return Redirect("/lockout");
            }
            else
            {
                ModelState.AddModelError("UserName", "Invalid login attempt.");
                return BadRequest(ModelState.ToFlatDictionary());
            }

            // If we got this far, something failed, redisplay form

        }
        /// <summary>
        /// Adds a hashtag to a user
        /// </summary>
        /// <param name="newTag"></param>
        /// <returns></returns>
        [HttpPost]
        public async Task<IActionResult> AddHashTag([FromBody] string newTag)
        {

            var res = await _mediator.Send(new AddHashtagCommand(newTag));
            if (!res.IsSucess)
            {
                return BadRequest(res.Errors);
            }
            return Ok();
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] RegisterCommand model)
        {

            var res = await _mediator.Send(model);
            if (res.IsSucess)
                return Ok();
            return BadRequest(res.Errors);

        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            _logger.LogInformation("User logged out.");
            return Redirect("/login");

        }

        [HttpPost]
        [AllowAnonymous]
        public IActionResult ExternalLogin(string provider, string returnUrl = null)
        {
            // Request a redirect to the external login provider.
            var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Account", new { returnUrl });
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);

            return Challenge(properties, provider);
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> ExternalLoginCallback(string returnUrl = null, string remoteError = null)
        {
            if (remoteError != null)
            {
                return Redirect("/login");
            }
            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                return Redirect("/login");
            }

            // Sign in the user with this external login provider if the user already has a login.
            var result = await _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);
            if (result.Succeeded)
            {
                _logger.LogInformation("User logged in with {Name} provider.", info.LoginProvider);
                return RedirectToLocal(returnUrl);
            }
            if (result.IsLockedOut)
            {
                return Redirect("/account/lockout");
            }
            else if (result.IsNotAllowed)
            {
                return Redirect("/login");

            }
            else
            {
                // If the user does not have an account, then ask the user to create an account.

                return Redirect("/account/externalLogin");
            }
        }
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> ExternalLoginDetails()
        {
            // Get the information about the user from the external login provider
            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                throw new ApplicationException("Error loading external login information during confirmation.");
            }
            return Ok(new ExternalLoginViewModel()
            {
                Email = info.Principal.FindFirstValue(ClaimTypes.Email),
                Provider = info.LoginProvider
            });
        }
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> ExternalLoginConfirmation([FromBody] ExternalLoginViewModel model)
        {

            // Get the information about the user from the external login provider
            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                throw new ApplicationException("Error loading external login information during confirmation.");
            }
            var user = new ApplicationUser { UserName = model.Email, Email = model.Email, EmailConfirmed = true };
            var result = await _userManager.CreateAsync(user);
            if (result.Succeeded)
            {
                result = await _userManager.AddLoginAsync(user, info);
                if (result.Succeeded)
                {
                    await _signInManager.SignInAsync(user, isPersistent: false);
                    _logger.LogInformation("User created an account using {Name} provider.", info.LoginProvider);
                    return Ok();
                }
            }
            return BadRequest(result.ToFlatDictionary());

        }

        private IActionResult RedirectToLocal(string returnUrl)
        {
            if (Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            else
            {
                return Redirect("/");
            }
        }
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> ConfirmEmail(string userId, string code)
        {
            if (userId == null || code == null)
            {
                return BadRequest();
            }
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return BadRequest();
            }
            var result = await _userManager.ConfirmEmailAsync(user, code);
            return result.Succeeded ? (IActionResult)Ok() : BadRequest();
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordViewModel model)
        {

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null || !(await _userManager.IsEmailConfirmedAsync(user)))
            {
                // Don't reveal that the user does not exist or is not confirmed
                return Ok();
            }

            // For more information on how to enable account confirmation and password reset please
            // visit https://go.microsoft.com/fwlink/?LinkID=532713
            var code = await _userManager.GeneratePasswordResetTokenAsync(user);
            var callbackUrl = Url.ResetPasswordCallbackLink(user.Id, code, Request.Scheme);
            await _emailSender.SendEmailAsync(model.Email, "Reset Password",
               $"Please reset your password by clicking here: <a href='{callbackUrl}'>link</a>");
            return Ok();
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPassword([FromBody]ResetPasswordViewModel model)
        {
            model.Code = WebUtility.UrlDecode(model.Code);
           
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                // Don't reveal that the user does not exist
                return Ok();
            }
            var result = await _userManager.ResetPasswordAsync(user, model.Code, model.Password);
            if (result.Succeeded)
            {
                return Ok();
            }

            return BadRequest(result.ToFlatDictionary());
        }

        [HttpGet]
        public async Task<IActionResult> Details()
        {
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
            {
                return Unauthorized();
            }

            var model = new AccountViewModel
            {
                HasPassword = !string.IsNullOrEmpty(user.PasswordHash),
                Email = user.Email,
                IsEmailConfirmed = user.EmailConfirmed,
                Hashtags = user.Hashtags?.ToList() ?? new System.Collections.Generic.List<string>()
            };

            return Ok(model);
        }

        [HttpPost]
        public async Task<IActionResult> Edit([FromBody] AccountViewModel model)
        {
          

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                throw new ApplicationException($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            var email = user.Email;
            if (model.Email != email)
            {
                var setEmailResult = await _userManager.SetEmailAsync(user, model.Email);
                if (!setEmailResult.Succeeded)
                {
                    throw new ApplicationException($"Unexpected error occurred setting email for user with ID '{user.Id}'.");
                }
            }
            return new OkResult();
        }



        [HttpPost]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordCommand model)
        {
           
            await _mediator.Send(model);

            return RedirectToAction(nameof(ChangePassword));
        }
    }
}
