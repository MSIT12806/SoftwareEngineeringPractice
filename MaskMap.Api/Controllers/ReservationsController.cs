using MaskMap.Api.Domains;
using Microsoft.AspNetCore.Mvc;

namespace MaskMap.Api.Controllers
{
    [ApiController]
    [Route("api/reservations")]
    public class ReservationsController : ControllerBase
    {
        private readonly ReservationService _reservationService;

        public ReservationsController(ReservationService reservationService)
        {
            _reservationService = reservationService;
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id, CancellationToken cancellationToken)
        {
            var reservation = await _reservationService.GetByIdAsync(id, cancellationToken);
            return reservation is null ? NotFound() : Ok(reservation);
        }

        [HttpPost]
        public async Task<IActionResult> Create(
            [FromBody] ReservationRequest request,
            CancellationToken cancellationToken)
        {
            if (!Request.Headers.TryGetValue("X-Test-User-Id", out var userId) ||
                string.IsNullOrWhiteSpace(userId))
            {
                return BadRequest(new { Code = "UserIdRequired", Message = "User id is required." });
            }

            var reservation = await _reservationService.CreateAsync(
                userId.ToString(),
                request.PharmacyId,
                request.ProductId,
                request.Quantity,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new { id = reservation.ReservationId },
                reservation);
        }
    }
}
