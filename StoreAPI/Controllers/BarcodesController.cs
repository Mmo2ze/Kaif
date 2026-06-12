using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreAPI.Services;
using StoreShared.Barcode;

namespace StoreAPI.Controllers;

[ApiController]
[Route("api/barcodes")]
[Authorize]
public sealed class BarcodesController : ControllerBase
{
    private readonly SkuBarcodeImageService _barcodePng;

    public BarcodesController(SkuBarcodeImageService barcodePng) => _barcodePng = barcodePng;

    /// <summary>Sample EAN-8 label for scanner/printer testing (not a catalog SKU).</summary>
    [HttpGet("test-ean8")]
    public ActionResult<BarcodeTestLabelDto> GetTestEan8Label([FromQuery] bool forLabelPrint = true)
    {
        var kind = forLabelPrint ? BarcodeImageKind.Label : BarcodeImageKind.Standard;
        var full = SkuBarcode.WithCheckDigit(SkuBarcode.TestArticle7);
        var png = _barcodePng.ToPngBase64(full, kind);
        return Ok(new BarcodeTestLabelDto(full, png));
    }
}
