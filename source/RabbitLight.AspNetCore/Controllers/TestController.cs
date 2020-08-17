using Microsoft.AspNetCore.Mvc;

namespace RabbitLight.AspNetCore.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class TestController : ControllerBase
    {
        [HttpGet]
        public string SendMessage()
        {
            return "Message published!";
        }
    }
}
