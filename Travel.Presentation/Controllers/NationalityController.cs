using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Travel.Domain;
using Travel.Domain.Models;
using Travel.Domain.Authorization;
using Travel.Domain.Specifications;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Controllers
{
    [Authorize]
    public class NationalityController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;

        public NationalityController(IUnitOfWork unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        [Authorize(Policy = Permissions.Nationalities.View)]
        public async Task<IActionResult> Index()
        {
            var spec = new BaseSpecifications<Nationality>();
            spec.SetOrderBy(n => n.Name);
            var nationalities = await _unitOfWork.Repository<Nationality>().GetAllAsync(spec);
            return View(nationalities);
        }

        [Authorize(Policy = Permissions.Nationalities.Create)]
        public IActionResult Create() => View(new NationalityViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Nationalities.Create)]
        public async Task<IActionResult> Create(NationalityViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var nationality = _mapper.Map<Nationality>(model);
            await _unitOfWork.Repository<Nationality>().AddAsync(nationality);
            await _unitOfWork.CompleteAsync();

            TempData["Success"] = "Nationality created successfully.";
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Policy = Permissions.Nationalities.Edit)]
        public async Task<IActionResult> Edit(int id)
        {
            var nationality = await _unitOfWork.Repository<Nationality>()
                .GetByIdAsync(new BaseSpecifications<Nationality>(n => n.Id == id));

            if (nationality is null)
                return NotFound();

            return View(_mapper.Map<NationalityViewModel>(nationality));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Nationalities.Edit)]
        public async Task<IActionResult> Edit(NationalityViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var nationality = await _unitOfWork.Repository<Nationality>()
                .GetByIdAsync(new BaseSpecifications<Nationality>(n => n.Id == model.Id));

            if (nationality is null)
                return NotFound();

            _mapper.Map(model, nationality);
            _unitOfWork.Repository<Nationality>().Update(nationality);
            await _unitOfWork.CompleteAsync();

            TempData["Success"] = "Nationality updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Permissions.Nationalities.Delete)]
        public async Task<IActionResult> Delete(int id)
        {
            var nationality = await _unitOfWork.Repository<Nationality>()
                .GetByIdAsync(new BaseSpecifications<Nationality>(n => n.Id == id));

            if (nationality is null)
                return Json(new { success = false, message = "Nationality not found." });

            // The FK is Restrict, so this guard is what turns an in-use delete into a readable
            // message instead of a raw SQL constraint error.
            var inUse = await _unitOfWork.Repository<Booking>()
                .GetAllAsync(new BaseSpecifications<Booking>(b => b.NationalityId == id));

            if (inUse.Any())
                return Json(new { success = false, message = "This nationality is used by one or more bookings and can't be deleted." });

            _unitOfWork.Repository<Nationality>().Delete(nationality);
            await _unitOfWork.CompleteAsync();

            return Json(new { success = true, message = "Nationality deleted successfully." });
        }

        // View, not Create — the Edit form calls this too.
        [AcceptVerbs("GET", "POST")]
        [Authorize(Policy = Permissions.Nationalities.View)]
        public async Task<IActionResult> IsNameAvailable(string name, int id)
        {
            if (string.IsNullOrWhiteSpace(name))
                return Json(true);

            var matches = await _unitOfWork.Repository<Nationality>()
                .GetAllAsync(new BaseSpecifications<Nationality>(n => n.Name == name && n.Id != id));

            return Json(!matches.Any());
        }
    }
}
