using System.ComponentModel.DataAnnotations;
using MediatR;

namespace TextServices.Builder.Api.Features.Jobs;

internal static class JobEndpoints
{
    internal static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/textbuilder", async (JobInstruction instruction, ISender sender) =>
        {
            var validationResults = new List<ValidationResult>();
            if (!Validator.TryValidateObject(instruction,
                    new ValidationContext(instruction), validationResults, validateAllProperties: true))
            {
                var errors = validationResults
                    .GroupBy(r => r.MemberNames.FirstOrDefault() ?? string.Empty)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(r => r.ErrorMessage ?? "Invalid").ToArray());
                return Results.ValidationProblem(errors);
            }

            var result = await sender.Send(new CreateJobRequest(instruction));

            if (result.AlreadyExists)
                return Results.Conflict(result.Response);

            return Results.Accepted($"/textbuilder/{instruction.Id}", result.Response);
        });

        routes.MapGet("/textbuilder", async (ISender sender, int page = 1, int pageSize = 20, string? status = null) =>
        {
            var result = await sender.Send(new ListJobsRequest(page, pageSize, status));
            return Results.Ok(result);
        });

        routes.MapGet("/textbuilder/{**id}", async (string id, ISender sender) =>
        {
            var response = await sender.Send(new GetJobRequest(id));
            return response == null ? Results.NotFound() : Results.Ok(response);
        });

        routes.MapPut("/textbuilder/{**id}", async (string id, ISender sender) =>
        {
            var result = await sender.Send(new ReprocessJobRequest(id));
            return result.Status switch
            {
                ReprocessStatus.NotFound => Results.NotFound(),
                ReprocessStatus.Conflict => Results.Conflict(result.Response),
                _ => Results.Accepted($"/textbuilder/{id}", result.Response),
            };
        });

        routes.MapDelete("/textbuilder/{**id}", async (string id, ISender sender) =>
        {
            var found = await sender.Send(new DeleteJobRequest(id));
            return found ? Results.NoContent() : Results.NotFound();
        });

        return routes;
    }
}
