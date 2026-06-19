using MaskMap.Api.Domains;
using Microsoft.AspNetCore.Mvc;

namespace MaskMap.Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ReservationsController : ControllerBase
    {
        private readonly ReservationDomainService _reservationDomainService;

        public ReservationsController(ReservationDomainService reservationDomainService)
        {
            _reservationDomainService = reservationDomainService;
        }

        [HttpGet("{id}")]
        public IActionResult GetById(string id)
        {
            var reservation = _reservationDomainService.GetById(id);
            return Ok(reservation);
        }
        [HttpPost]
        public IActionResult Create([FromBody] ReservationRequest req)
        {
            var reservation = _reservationDomainService.Create(req);
            return CreatedAtAction(nameof(GetById), new { reservation.ReservationId }, reservation);
        }
    }
}
