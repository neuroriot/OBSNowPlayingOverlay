using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace OAuthBackend.Controllers
{
    [Route("/")]
    [ApiController]
    public class IndexController : Controller
    {
        [EnableCors("allowGET")]
        [HttpGet]
        public IActionResult Index()
        {
            return Redirect("https://github.com/konnokai/OBSNowPlayingOverlay");
        }
    }
}
