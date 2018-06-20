﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Toss.Shared;
using Toss.Server.Models;
using MediatR;
using System.Threading;

namespace Toss.Server.Controllers
{
    public class LoginCommandHandler : IRequestHandler<LoginCommand, LoginCommandResult>
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger _logger;

        public async Task<LoginCommandResult> Handle(LoginCommand request, CancellationToken cancellationToken)
        {
            var result = await _signInManager.PasswordSignInAsync(request.UserName, request.Password, request.RememberMe, lockoutOnFailure: false);
            if (result.Succeeded)
            {
                _logger.LogInformation("User logged in.");
                return new LoginCommandResult() { IsSuccess = true };
            }
            if (result.RequiresTwoFactor)
            {
                return new LoginCommandResult() { Need2FA = true };
            }
            if (result.IsLockedOut)
            {
                _logger.LogWarning("User account locked out.");
                return new LoginCommandResult() { IsLockout = true };
            }
            else
            {
                return new LoginCommandResult() { IsSuccess = false };
            }
        }
    }
}
