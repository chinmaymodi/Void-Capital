using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VoidCapital.Api.Controllers;
using VoidCapital.Api.Modules.Portfolio.DTOs;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Shared;
using VoidCapital.Api.Shared.Repositories;
using Xunit;

namespace VoidCapital.Api.Tests.Controllers;

public class UsersControllerTests
{
    private readonly Mock<IUserRepository> _userRepo = new();

    private UsersController CreateController() => new(_userRepo.Object);

    [Fact]
    public async Task GetAll_ReturnsAllUsersAsIdNamePairs()
    {
        _userRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[]
        {
            new User { Id = 1, Name = "Trader One" },
            new User { Id = 2, Name = "System Portfolio" },
            new User { Id = 3, Name = "System-Reckless" }
        });

        var result = await CreateController().GetAll();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<IEnumerable<UserDto>>>().Subject;
        envelope.Success.Should().BeTrue();
        var users = envelope.Data!.ToList();
        users.Should().HaveCount(3);
        users[0].Should().Be(new UserDto(1, "Trader One"));
        users[2].Should().Be(new UserDto(3, "System-Reckless"));
    }

    [Fact]
    public async Task GetAll_NoUsers_ReturnsEmptyList()
    {
        _userRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(Array.Empty<User>());

        var result = await CreateController().GetAll();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<IEnumerable<UserDto>>>().Subject;
        envelope.Data.Should().BeEmpty();
    }
}