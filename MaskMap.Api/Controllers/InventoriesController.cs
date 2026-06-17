using Microsoft.AspNetCore.Mvc;

namespace MaskMap.Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class InventoriesController : ControllerBase
    {
        [HttpGet]
        public void Get() { }
    }
}
