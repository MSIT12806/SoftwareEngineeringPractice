using MaskMap.Api.Domains;
using Microsoft.AspNetCore.Mvc;

namespace MaskMap.Api.Controllers
{
    [ApiController]
    [Route("api/me/reservations")]
    public sealed class MeReservationsController : ControllerBase
    {
        private readonly ReservationService _reservationService;

        public MeReservationsController(ReservationService reservationService)
        {
            _reservationService = reservationService;
        }

        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken cancellationToken)
        {
            if (!Request.Headers.TryGetValue("X-Test-User-Id", out var userId) ||
                string.IsNullOrWhiteSpace(userId))
            {
                return BadRequest(new
                {
                    Code = "UserIdRequired",
                    Message = "User id is required."
                });
            }

            var reservations = await _reservationService.GetForUserAsync(
                userId.ToString(),
                cancellationToken);

            return Ok(new UserReservationsResponse(
                reservations.Select(reservation => new UserReservationResponse(
                    reservation.ReservationId,
                    reservation.Status,
                    reservation.PharmacyId,
                    reservation.ProductId,
                    reservation.Quantity,
                    reservation.ExpiresAt)).ToList()));
        }
    }
}
