using Microsoft.AspNetCore.Mvc;

namespace BaiiTap2.Controllers
{
    public class BioController : Controller
    {
        public IActionResult MyCard()
        {
            // Đường dẫn ảnh dùng relative path (đúng chuẩn ASP.NET Core)
            ViewBag.image = "/images/anhmacdinh.jpg";
            ViewBag.HoTen = "Nguyễn Khắc Hiếu";
            ViewBag.NgheNghiep = "Sinh viên CNTT";
            ViewBag.ChuyenNganh = "Full Stack Developer";
            ViewBag.NamSinh = 2006;
            ViewBag.IdCard = "03022005";

            return View();
        }
    }
}