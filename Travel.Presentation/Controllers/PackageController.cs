using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Travel.Domain;
using Travel.Domain.Models;
using Travel.Domain.Services;
using Travel.Domain.Specifications;
using Travel.Domain.Specifications.Packages;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.Controllers
{
    [Authorize]
    public class PackageController : Controller
    {
        private const string PackagesFolder = "Packages";

        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly IFileStorageService _fileStorage;

        public PackageController(IUnitOfWork unitOfWork, IMapper mapper, IFileStorageService fileStorage)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _fileStorage = fileStorage;
        }

        public async Task<IActionResult> Index()
        {
            var packages = await _unitOfWork.Repository<Package>().GetAllAsync(new PackageWithDetailsSpec());
            return View(packages);
        }

        public async Task<IActionResult> Details(int id)
        {
            var package = await _unitOfWork.Repository<Package>().GetByIdAsync(new PackageWithDetailsSpec(id));
            if (package is null)
                return NotFound();

            return View(package);
        }

        public async Task<IActionResult> Create()
        {
            var model = new PackageViewModel();
            await PopulateOptionsAsync(model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PackageViewModel model)
        {
            await ValidatePackageAsync(model);

            if (!ModelState.IsValid)
            {
                await PopulateOptionsAsync(model);
                return View(model);
            }

            var package = _mapper.Map<Package>(model);
            package.CreatedAt = DateTime.UtcNow;
            package.ImageUrl = await SaveCoverImageAsync(model.CoverImage);
            package.Segments = BuildSegments(model.Segments);

            await _unitOfWork.Repository<Package>().AddAsync(package);
            await _unitOfWork.CompleteAsync();

            TempData["Success"] = "Package created successfully.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int id)
        {
            var package = await _unitOfWork.Repository<Package>().GetByIdAsync(new PackageWithDetailsSpec(id));
            if (package is null)
                return NotFound();

            var model = _mapper.Map<PackageViewModel>(package);
            await PopulateOptionsAsync(model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(PackageViewModel model)
        {
            await ValidatePackageAsync(model);

            if (!ModelState.IsValid)
            {
                await PopulateOptionsAsync(model);
                return View(model);
            }

            var package = await _unitOfWork.Repository<Package>().GetByIdAsync(new PackageWithDetailsSpec(model.Id));
            if (package is null)
                return NotFound();

            // Scalar fields (ImageUrl/Segments are ignored by the map and handled below).
            _mapper.Map(model, package);

            if (model.CoverImage is not null && model.CoverImage.Length > 0)
            {
                if (!string.IsNullOrEmpty(package.ImageUrl))
                    _fileStorage.DeleteFile(Path.GetFileName(package.ImageUrl), PackagesFolder);

                package.ImageUrl = await SaveCoverImageAsync(model.CoverImage);
            }

            // Replace segments — delete the old, add the new (atomic in one SaveChanges).
            foreach (var old in package.Segments.ToList())
                _unitOfWork.Repository<PackageSegment>().Delete(old);

            package.Segments = BuildSegments(model.Segments);

            _unitOfWork.Repository<Package>().Update(package);
            await _unitOfWork.CompleteAsync();

            TempData["Success"] = "Package updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var package = await _unitOfWork.Repository<Package>().GetByIdAsync(new PackageWithDetailsSpec(id));
            if (package is null)
                return Json(new { success = false, message = "Package not found." });

            if (!string.IsNullOrEmpty(package.ImageUrl))
                _fileStorage.DeleteFile(Path.GetFileName(package.ImageUrl), PackagesFolder);

            _unitOfWork.Repository<Package>().Delete(package);
            await _unitOfWork.CompleteAsync();

            return Json(new { success = true, message = "Package deleted successfully." });
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private static List<PackageSegment> BuildSegments(IEnumerable<PackageSegmentInput> inputs) =>
            (inputs ?? Enumerable.Empty<PackageSegmentInput>())
                .Select((s, i) => new PackageSegment
                {
                    DestinationId = s.DestinationId,
                    Nights = s.Nights,
                    SortOrder = i
                })
                .ToList();

        private async Task<string?> SaveCoverImageAsync(IFormFile? image)
        {
            if (image is null || image.Length == 0)
                return null;

            await using var stream = image.OpenReadStream();
            var fileName = await _fileStorage.SaveFileAsync(stream, image.FileName, PackagesFolder);
            return $"/Files/{PackagesFolder}/{fileName}";
        }

        // Base must be a real, non-cruise destination; cover image (if any) must be an image;
        // a cruise package must have a fixed total (the embark math needs an anchor).
        private async Task ValidatePackageAsync(PackageViewModel model)
        {
            if (model.CoverImage is not null && model.CoverImage.Length > 0 && !_fileStorage.IsImage(model.CoverImage.FileName))
                ModelState.AddModelError(nameof(model.CoverImage), "Only image files are allowed.");

            if (model.BaseDestinationId > 0)
            {
                var baseDest = await _unitOfWork.Repository<Destination>()
                    .GetByIdAsync(new BaseSpecifications<Destination>(d => d.Id == model.BaseDestinationId));

                if (baseDest is null)
                    ModelState.AddModelError(nameof(model.BaseDestinationId), "Selected base destination doesn't exist.");
                else if (baseDest.IsCruise)
                    ModelState.AddModelError(nameof(model.BaseDestinationId), "The base destination can't be a cruise — guests arrive and depart there.");
            }

            var segmentDestinationIds = model.Segments.Select(s => s.DestinationId).Distinct().ToList();
            if (model.TotalNights is null && segmentDestinationIds.Count > 0)
            {
                var cruiseSpec = new BaseSpecifications<Destination>(d => segmentDestinationIds.Contains(d.Id) && d.IsCruise);
                var hasCruiseSegment = (await _unitOfWork.Repository<Destination>().GetAllAsync(cruiseSpec)).Any();
                if (hasCruiseSegment)
                    ModelState.AddModelError(nameof(model.TotalNights),
                        "A package with a cruise segment must have a fixed total nights — the cruise embark schedule needs it.");
            }
        }

        private async Task PopulateOptionsAsync(PackageViewModel model)
        {
            var spec = new BaseSpecifications<Destination>(d => d.IsActive);
            spec.SetOrderBy(d => d.Name);
            var destinations = (await _unitOfWork.Repository<Destination>().GetAllAsync(spec)).ToList();

            model.BaseDestinationOptions = destinations
                .Where(d => !d.IsCruise)
                .Select(d => new SelectListItem(d.Name, d.Id.ToString()))
                .ToList();

            model.SegmentDestinationOptions = destinations
                .Select(d => new SelectListItem(d.IsCruise ? $"{d.Name} (cruise)" : d.Name, d.Id.ToString()))
                .ToList();
        }
    }
}
