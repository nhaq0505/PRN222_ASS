using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitTrip.Services.Trips;
using SplitTrip.Services.Trips.Dtos;
using SplitTrip.Web.Infrastructure;

namespace SplitTrip.Web.Pages.Trips;

[Authorize]
public sealed class CreateModel(ITripService tripService) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new()
    {
        StartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
        EndDate = DateOnly.FromDateTime(DateTime.Today.AddDays(9)),
        EstimatedBudgetPerPerson = 2_500_000m
    };

    public string? ErrorMessage { get; private set; }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Vui lòng nhập tên chuyến đi.")]
        [StringLength(200, MinimumLength = 2, ErrorMessage = "Tên chuyến đi từ 2 đến 200 ký tự.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập điểm đến.")]
        [StringLength(200, MinimumLength = 2, ErrorMessage = "Điểm đến từ 2 đến 200 ký tự.")]
        public string Destination { get; set; } = string.Empty;

        [StringLength(2000, ErrorMessage = "Mô tả tối đa 2000 ký tự.")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn ngày bắt đầu.")]
        [DataType(DataType.Date)]
        public DateOnly StartDate { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn ngày kết thúc.")]
        [DataType(DataType.Date)]
        public DateOnly EndDate { get; set; }

        [Range(0, 1_000_000_000, ErrorMessage = "Ngân sách dự kiến phải từ 0đ trở lên.")]
        public decimal EstimatedBudgetPerPerson { get; set; }
    }

    public IActionResult OnGet()
    {
        if (User.IsSystemAdmin())
        {
            return RedirectToPage("/Admin/Index");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (User.IsSystemAdmin())
        {
            return RedirectToPage("/Admin/Index");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var userId = User.GetRequiredUserId();
            var created = await tripService.CreateTripAsync(
                userId,
                new CreateTripRequest(
                    Input.Name,
                    Input.Destination,
                    Input.Description,
                    Input.StartDate,
                    Input.EndDate,
                    Input.EstimatedBudgetPerPerson),
                ct);

            TempData["SuccessMessage"] = $"Đã tạo chuyến đi \"{created.Name}\"! Mã mời của nhóm là {created.InviteCode}.";
            return RedirectToPage("/Trips/Details", new { id = created.Id });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }
}
