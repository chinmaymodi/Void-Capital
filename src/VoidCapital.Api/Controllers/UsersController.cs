using Microsoft.AspNetCore.Mvc;
using VoidCapital.Api.Modules.Portfolio.DTOs;
using VoidCapital.Api.Shared;
using VoidCapital.Api.Shared.Repositories;

namespace VoidCapital.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserRepository _userRepo;

    public UsersController(IUserRepository userRepo)
    {
        _userRepo = userRepo;
    }

    /// <summary>All users (id + name) for the frontend user picker.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<UserDto>>>> GetAll()
    {
        var users = await _userRepo.GetAllAsync();
        var dtos = users.Select(u => new UserDto(u.Id, u.Name)).ToList();
        return Ok(ApiResponse<IEnumerable<UserDto>>.Ok(dtos));
    }
}