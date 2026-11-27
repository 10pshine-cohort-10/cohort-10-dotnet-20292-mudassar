using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskManager.Api.DTOs;
using TaskManager.Api.Exceptions;
using TaskManager.Api.Extensions;
using TaskManager.Api.Services;

namespace TaskManager.Api.Controllers;

[ApiController]
[Route("api/tasks")]
[Produces("application/json")]
[Authorize]
public class TaskTransferController : ControllerBase
{
    private const long MaxImportBytes = 2 * 1024 * 1024;
    private readonly ITaskTransferService transfer;

    public TaskTransferController(ITaskTransferService transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        this.transfer = transfer;
    }

    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Export(
        [FromQuery] string format = "json",
        [FromQuery] TaskFilterParams? filter = null)
    {
        var exported = await transfer.ExportAsync(
            User.GetUserId(),
            User.IsAdmin(),
            format,
            filter ?? new TaskFilterParams(),
            HttpContext.RequestAborted);

        return File(exported.Content, exported.ContentType, exported.FileName);
    }

    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxImportBytes)]
    [ProducesResponseType<TaskImportResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TaskImportResultDto>> Import(IFormFile? file)
    {
        if (file is null || file.Length == 0)
            throw new AppValidationException("Please select a non-empty .json or .csv file to import.");

        await using var stream = file.OpenReadStream();
        var result = await transfer.ImportAsync(
            stream,
            file.FileName,
            User.GetUserId(),
            HttpContext.RequestAborted);

        return Ok(result);
    }
}

