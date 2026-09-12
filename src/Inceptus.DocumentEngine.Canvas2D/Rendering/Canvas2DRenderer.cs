using Inceptus.DocumentEngine.Canvas2D.Rendering.Interop;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Text;
using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.Canvas2D.Rendering;

/// <summary>
/// The single concrete version 1 graphics executor for one physical HTML canvas.
/// </summary>
public sealed class Canvas2DRenderer : ITextMetricsService, IAsyncDisposable
{
    private const int TextMeasurementCacheCapacity = 2048;
    private readonly ICanvas2DRenderExecution _execution;
    private readonly Canvas2DRendererConfiguration _configuration;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly Dictionary<TextMeasurementRequest, TextMeasurementResult>
        _textMeasurementCache = [];
    private bool _isInitialized;
    private bool _isDisposed;
    private Diagnostic? _disposalDiagnostic;

    public Canvas2DRenderer(
        IJSRuntime jsRuntime,
        Canvas2DRendererConfiguration? configuration = null)
        : this(new Canvas2DJsInterop(jsRuntime), configuration)
    {
    }

    internal Canvas2DRenderer(
        ICanvas2DRenderExecution execution,
        Canvas2DRendererConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(execution);
        _execution = execution;
        _configuration = configuration ?? Canvas2DRendererConfiguration.Default;
    }

    public bool IsInitialized => Volatile.Read(ref _isInitialized);

    public bool IsDisposed => Volatile.Read(ref _isDisposed);

    /// <summary>
    /// Gets the bounded immutable diagnostic snapshot for best-effort browser-resource disposal.
    /// The snapshot is empty after successful disposal and contains exactly one entry after failure.
    /// </summary>
    public System.Collections.Immutable.ImmutableArray<Diagnostic> DisposalDiagnostics
    {
        get
        {
            var diagnostic = Volatile.Read(ref _disposalDiagnostic);
            return diagnostic is null ? [] : [diagnostic];
        }
    }

    public async ValueTask<Canvas2DRendererResult> InitializeAsync(
        string canvasElementId,
        Canvas2DSurfaceSize surfaceSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(canvasElementId))
        {
            return Failure(
                Canvas2DRendererDiagnosticCodes.InitializationFailed,
                "A non-empty HTML canvas element identifier is required.",
                nameof(canvasElementId));
        }
        if (!await TryEnterAsync(cancellationToken))
        {
            return CancelledResult("Initialize");
        }

        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return CancelledResult("Initialize");
            }

            if (_isDisposed)
            {
                return Failure(Canvas2DRendererDiagnosticCodes.Disposed, "Canvas2DRenderer is disposed.");
            }

            if (_isInitialized)
            {
                return Failure(
                    Canvas2DRendererDiagnosticCodes.AlreadyInitialized,
                    "Canvas2DRenderer is already initialized.");
            }

            if (!IsValidSurface(surfaceSize))
            {
                return Failure(
                    Canvas2DRendererDiagnosticCodes.InitializationFailed,
                    "Canvas2D surface dimensions and device-pixel ratio must be finite, positive, and representable.",
                    nameof(surfaceSize));
            }

            var result = await InvokeInteropAsync(
                () => _execution.InitializeAsync(
                    canvasElementId,
                    surfaceSize,
                    _configuration.ImageResources,
                    _configuration.FontResources,
                    _configuration.DefaultFontFamily),
                Canvas2DRendererDiagnosticCodes.InitializationFailed,
                "Canvas2DRenderer initialization failed.");
            if (result.Succeeded)
            {
                Volatile.Write(ref _isInitialized, true);
            }

            return result;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask<Canvas2DRendererResult> ResizeAsync(
        Canvas2DSurfaceSize surfaceSize,
        CancellationToken cancellationToken = default)
    {
        if (!await TryEnterAsync(cancellationToken))
        {
            return CancelledResult("Resize");
        }

        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return CancelledResult("Resize");
            }

            var lifecycleFailure = ValidateActiveRenderer();
            if (lifecycleFailure is not null)
            {
                return lifecycleFailure;
            }

            if (!IsValidSurface(surfaceSize))
            {
                return Failure(
                    Canvas2DRendererDiagnosticCodes.ResizeFailed,
                    "Canvas2D surface dimensions and device-pixel ratio must be finite, positive, and representable.",
                    nameof(surfaceSize));
            }

            return await InvokeInteropAsync(
                () => _execution.ResizeAsync(surfaceSize),
                Canvas2DRendererDiagnosticCodes.ResizeFailed,
                "Canvas2D canvas resizing failed.");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask<Canvas2DRendererResult> RenderAsync(
        Canvas2DScene scene,
        CancellationToken cancellationToken = default)
    {
        if (scene is null)
        {
            return Failure(
                Canvas2DRendererDiagnosticCodes.InvalidScene,
                "A complete immutable Canvas2DScene is required.",
                nameof(scene));
        }
        if (!await TryEnterAsync(cancellationToken))
        {
            return CancelledResult("Render");
        }

        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return CancelledResult("Render");
            }

            var lifecycleFailure = ValidateActiveRenderer();
            if (lifecycleFailure is not null)
            {
                return lifecycleFailure;
            }

            var imageFailure = ValidateImageResources(scene);
            if (imageFailure is not null)
            {
                return imageFailure;
            }

            var fontFailure = ValidateFontResources(scene);
            if (fontFailure is not null)
            {
                return fontFailure;
            }

            Canvas2DRenderFrame frame;
            try
            {
                var items = new Canvas2DRenderItem[scene.Items.Length];
                for (var index = 0; index < scene.Items.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    items[index] = new Canvas2DRenderItem(scene.Items[index]);
                }

                frame = new Canvas2DRenderFrame(scene.ViewportTransform, items);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return CancelledResult("Render");
            }
#pragma warning disable CA1031 // A malformed immutable scene becomes a stable presentation diagnostic.
            catch (Exception exception) when (IsNonFatal(exception))
            {
                return UnexpectedFailure(
                    Canvas2DRendererDiagnosticCodes.InvalidScene,
                    "Canvas2DScene could not be converted into a complete frame.",
                    exception,
                    scene.DocumentId.Value);
            }
#pragma warning restore CA1031

            // Cancellation intentionally stops here. Once dispatched, one complete-frame browser
            // operation is allowed to finish so no transactional rollback semantics are implied.
            return await InvokeInteropAsync(
                () => _execution.RenderAsync(frame),
                Canvas2DRendererDiagnosticCodes.RenderingFailed,
                "Canvas2D complete-frame rendering failed.");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Converts canvas-relative CSS coordinates into logical document coordinates.
    /// </summary>
    public static PointD ConvertCssToDocument(Canvas2DScene scene, PointD cssPoint)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!scene.ViewportTransform.TryInvert(out var inverse))
        {
            throw new InvalidOperationException("The Canvas2DScene viewport transform is not invertible.");
        }

        return inverse.TransformPoint(cssPoint);
    }

    public async ValueTask<TextMeasurementResult> MeasureAsync(
        TextMeasurementRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return TextMeasurementResult.Failure(
            [
                TextError(
                    TextMetricsDiagnosticCodes.InvalidRequest,
                    "A complete immutable text-measurement request is required.",
                    nameof(request)),
            ]);
        }
        if (!StringComparer.Ordinal.Equals(
                request.ConfigurationId,
                _configuration.TextMeasurementConfigurationId) ||
            !StringComparer.Ordinal.Equals(
                request.ConfigurationVersion,
                _configuration.TextMeasurementConfigurationVersion))
        {
            return TextMeasurementResult.Failure(
            [
                TextError(
                    TextMetricsDiagnosticCodes.UnsupportedConfiguration,
                    "The text-measurement configuration is not supported by this Canvas2DRenderer.",
                    request.ConfigurationId),
            ]);
        }

        if (!StringComparer.Ordinal.Equals(request.Locale, "und"))
        {
            return TextMeasurementResult.Failure(
            [
                TextError(
                    TextMetricsDiagnosticCodes.UnsupportedConfiguration,
                    "Canvas2D text measurement supports only the locale-neutral 'und' locale.",
                    request.Locale),
            ]);
        }

        var font = _configuration.FontResources.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.FontIdentity, request.FontIdentity) &&
            StringComparer.Ordinal.Equals(candidate.FontVersion, request.FontVersion) &&
            candidate.FontWeight == request.FontWeight &&
            candidate.FontStyle == request.FontStyle);
        if (font is null || !StringComparer.Ordinal.Equals(font.FontFamily, request.FontFamily))
        {
            return TextMeasurementResult.Failure(
            [
                TextError(
                    TextMetricsDiagnosticCodes.UnavailableFont,
                    "The requested stable font identity and version are not configured for this Canvas2DRenderer.",
                    request.FontIdentity),
            ]);
        }

        if (!await TryEnterAsync(cancellationToken))
        {
            return CancelledMeasurement(request.FontFamily);
        }

        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return CancelledMeasurement(request.FontFamily);
            }

            if (_isDisposed || !_isInitialized)
            {
                return TextMeasurementResult.Failure(
                [
                    TextError(
                        TextMetricsDiagnosticCodes.MeasurementFailure,
                        _isDisposed
                            ? "Canvas2DRenderer is disposed."
                            : "Canvas2DRenderer is not initialized.",
                        request.FontFamily),
                ]);
            }

            if (_textMeasurementCache.TryGetValue(request, out var cached))
            {
                return cached;
            }

            Canvas2DTextMeasurementInteropResult result;
#pragma warning disable CA1031 // Browser failures become stable text-measurement diagnostics.
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                result = await _execution.MeasureTextAsync(
                    new Canvas2DTextMeasurementRequestData(request));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return CancelledMeasurement(request.FontFamily);
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                return TextMeasurementResult.Failure(
                [
                    TextError(
                        TextMetricsDiagnosticCodes.MeasurementFailure,
                        "Canvas2D browser text measurement failed.",
                        request.FontFamily,
                        exception),
                ]);
            }
#pragma warning restore CA1031

            if (!result.Succeeded)
            {
                var code = result.Code switch
                {
                    TextMetricsDiagnosticCodes.UnavailableFont =>
                        TextMetricsDiagnosticCodes.UnavailableFont,
                    TextMetricsDiagnosticCodes.UnsupportedConfiguration =>
                        TextMetricsDiagnosticCodes.UnsupportedConfiguration,
                    _ => TextMetricsDiagnosticCodes.MeasurementFailure,
                };
                var message = code switch
                {
                    TextMetricsDiagnosticCodes.UnavailableFont =>
                        "The requested browser font is unavailable.",
                    TextMetricsDiagnosticCodes.UnsupportedConfiguration =>
                        "The requested browser text configuration is unsupported.",
                    _ => "Canvas2D browser text measurement failed.",
                };
                return TextMeasurementResult.Failure(
                [
                    TextError(code, message, result.SourceIdentity ?? request.FontFamily),
                ]);
            }


            var expectedResolvedFontIdentity = $"{request.FontIdentity}@{request.FontVersion}";
            if (!StringComparer.Ordinal.Equals(
                    result.ResolvedFontIdentity,
                    expectedResolvedFontIdentity))
            {
                return TextMeasurementResult.Failure(
                [
                    TextError(
                        TextMetricsDiagnosticCodes.FontSubstituted,
                        "The browser did not resolve the explicitly configured font identity and version.",
                        request.FontIdentity),
                ]);
            }

#pragma warning disable CA1031 // Malformed browser data becomes a stable failure with no partial metrics.
            try
            {
                var metrics = new TextMetrics(
                    result.Width,
                    result.Ascent,
                    result.Descent,
                    result.LineHeight,
                    new RectD(
                        result.BoundingX,
                        result.BoundingY,
                        result.BoundingWidth,
                        result.BoundingHeight),
                    result.ResolvedFontIdentity!);
                var successful = TextMeasurementResult.Success(metrics);
                if (_textMeasurementCache.Count >= TextMeasurementCacheCapacity)
                {
                    _textMeasurementCache.Clear();
                }

                _textMeasurementCache.Add(request, successful);
                return successful;
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                return TextMeasurementResult.Failure(
                [
                    TextError(
                        TextMetricsDiagnosticCodes.MeasurementFailure,
                        "Canvas2D browser text measurement returned invalid metrics.",
                        request.FontFamily,
                        exception),
                ]);
            }
#pragma warning restore CA1031
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Creates the canonical logical-unit request used by Scene text layout from this
    /// renderer's configured browser font resource and measurement contract.
    /// </summary>
    internal TextMeasurementRequest CreateTextMeasurementRequest(
        string text,
        Canvas2DSceneStyle style,
        double lineHeight)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(style);
        var family = style.FontFamily ?? _configuration.DefaultFontFamily;
        var font = family is null
            ? null
            : _configuration.FontResources.FirstOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.FontFamily, family) &&
                candidate.FontWeight == 400 &&
                candidate.FontStyle == TextFontStyle.Normal);
        if (font is null)
        {
            throw new InvalidOperationException(
                "Canvas2D Scene text layout requires the renderer's configured normal-weight default font.");
        }

        return new TextMeasurementRequest(
            text,
            font.FontFamily,
            font.FontIdentity,
            font.FontVersion,
            style.FontSize,
            lineHeight,
            font.FontWeight,
            font.FontStyle,
            "und",
            TextDirection.LeftToRight,
            TextWritingMode.HorizontalTopToBottom,
            1d,
            _configuration.TextMeasurementConfigurationId,
            _configuration.TextMeasurementConfigurationVersion);
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            if (_isDisposed)
            {
                return;
            }

            Volatile.Write(ref _isDisposed, true);
            Volatile.Write(ref _isInitialized, false);
            _textMeasurementCache.Clear();
#pragma warning disable CA1031 // Disposal is best effort after the public object is permanently disposed.
            try
            {
                await _execution.DisposeAsync();
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                Volatile.Write(
                    ref _disposalDiagnostic,
                    Error(
                        Canvas2DRendererDiagnosticCodes.ResourceDisposalFailed,
                        "One or more Canvas2D browser resources could not be disposed cleanly.",
                        "Canvas2DRenderer",
                        new KeyValuePair<string, string>(
                            "ExceptionType",
                            exception.GetType().FullName ?? exception.GetType().Name)));
            }
#pragma warning restore CA1031
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async ValueTask<bool> TryEnterAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _lifecycleGate.WaitAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private Canvas2DRendererResult? ValidateActiveRenderer()
    {
        if (_isDisposed)
        {
            return Failure(Canvas2DRendererDiagnosticCodes.Disposed, "Canvas2DRenderer is disposed.");
        }

        return !_isInitialized
            ? Failure(
                Canvas2DRendererDiagnosticCodes.NotInitialized,
                "Canvas2DRenderer is not initialized.")
            : null;
    }

    private Canvas2DRendererResult? ValidateImageResources(Canvas2DScene scene)
    {
        foreach (var item in scene.Items)
        {
            if (!item.IsVisible || item.Geometry.Kind != Canvas2DSceneGeometryKind.Image)
            {
                continue;
            }

            var imageReference = item.Geometry.Content!;
            if (!_configuration.ImageResources.ContainsKey(imageReference))
            {
                return Failure(
                    Canvas2DRendererDiagnosticCodes.MissingImageResource,
                    "Canvas2DScene references an image without a configured browser resource.",
                    item.Id.Value,
                    new KeyValuePair<string, string>("ImageReference", imageReference));
            }
        }

        return null;
    }

    private Canvas2DRendererResult? ValidateFontResources(Canvas2DScene scene)
    {
        foreach (var item in scene.Items)
        {
            if (!item.IsVisible || item.Geometry.Kind != Canvas2DSceneGeometryKind.Text)
            {
                continue;
            }

            var family = item.Style.FontFamily ?? _configuration.DefaultFontFamily;
            if (family is null || !_configuration.FontResources.Any(font =>
                    StringComparer.Ordinal.Equals(font.FontFamily, family) &&
                    font.FontWeight == 400 &&
                    font.FontStyle == TextFontStyle.Normal))
            {
                return Failure(
                    Canvas2DRendererDiagnosticCodes.FontUnavailable,
                    "Canvas2DScene text requires an explicitly configured browser font resource.",
                    item.Id.Value,
                    new KeyValuePair<string, string>("FontFamily", family ?? "<unspecified>"));
            }
        }

        return null;
    }

    private static bool IsValidSurface(Canvas2DSurfaceSize surfaceSize)
    {
        var backingWidth = surfaceSize.CssWidth * surfaceSize.DevicePixelRatio;
        var backingHeight = surfaceSize.CssHeight * surfaceSize.DevicePixelRatio;
        return double.IsFinite(surfaceSize.CssWidth) && surfaceSize.CssWidth > 0d &&
            double.IsFinite(surfaceSize.CssHeight) && surfaceSize.CssHeight > 0d &&
            double.IsFinite(surfaceSize.DevicePixelRatio) && surfaceSize.DevicePixelRatio > 0d &&
            double.IsFinite(backingWidth) && backingWidth > 0d && backingWidth <= int.MaxValue &&
            double.IsFinite(backingHeight) && backingHeight > 0d && backingHeight <= int.MaxValue;
    }

    private static async ValueTask<Canvas2DRendererResult> InvokeInteropAsync(
        Func<ValueTask<Canvas2DInteropOperationResult>> operation,
        string fallbackCode,
        string fallbackMessage)
    {
#pragma warning disable CA1031 // Browser failures become stable renderer diagnostics.
        try
        {
            var result = await operation();
            if (result.Succeeded)
            {
                return Canvas2DRendererResult.Success();
            }

            var code = NormalizeInteropCode(result.Code, fallbackCode);
            return Failure(
                code,
                DescribeInteropFailure(code, fallbackMessage),
                result.SourceIdentity ?? "Canvas2DRenderer");
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return UnexpectedFailure(
                fallbackCode,
                fallbackMessage,
                exception,
                "Canvas2DRenderer");
        }
#pragma warning restore CA1031
    }

    private static string NormalizeInteropCode(string? code, string fallbackCode) => code switch
    {
        Canvas2DRendererDiagnosticCodes.CanvasNotFound => code,
        Canvas2DRendererDiagnosticCodes.ContextUnavailable => code,
        Canvas2DRendererDiagnosticCodes.AlreadyInitialized => code,
        Canvas2DRendererDiagnosticCodes.Disposed => code,
        Canvas2DRendererDiagnosticCodes.ResizeFailed => code,
        Canvas2DRendererDiagnosticCodes.ImageLoadFailed => code,
        Canvas2DRendererDiagnosticCodes.RenderingFailed => code,
        _ => fallbackCode,
    };

    private static string DescribeInteropFailure(string code, string fallbackMessage) => code switch
    {
        Canvas2DRendererDiagnosticCodes.CanvasNotFound =>
            "The configured HTML canvas was not found.",
        Canvas2DRendererDiagnosticCodes.ContextUnavailable =>
            "The HTML canvas did not provide a CanvasRenderingContext2D.",
        Canvas2DRendererDiagnosticCodes.AlreadyInitialized =>
            "The HTML canvas is already owned by another Canvas2DRenderer.",
        Canvas2DRendererDiagnosticCodes.Disposed =>
            "The Canvas2D browser renderer is disposed.",
        Canvas2DRendererDiagnosticCodes.ResizeFailed =>
            "Canvas2D canvas resizing failed.",
        Canvas2DRendererDiagnosticCodes.ImageLoadFailed =>
            "A configured Canvas2D image resource could not be loaded.",
        Canvas2DRendererDiagnosticCodes.RenderingFailed =>
            "Canvas2D complete-frame rendering failed.",
        _ => fallbackMessage,
    };

    private static Canvas2DRendererResult CancelledResult(string operation) =>
        Canvas2DRendererResult.Cancelled(
            Error(
                Canvas2DRendererDiagnosticCodes.Cancelled,
                $"Canvas2D renderer {operation} was cancelled before browser dispatch.",
                operation));

    private static TextMeasurementResult CancelledMeasurement(string sourceIdentity) =>
        TextMeasurementResult.Cancelled(
        [
            TextError(
                TextMetricsDiagnosticCodes.Cancelled,
                "Canvas2D text measurement was cancelled before browser dispatch.",
                sourceIdentity),
        ]);

    private static Canvas2DRendererResult Failure(
        string code,
        string message,
        string sourceIdentity = "Canvas2DRenderer",
        params KeyValuePair<string, string>[] context) =>
        Canvas2DRendererResult.Failure(
        [
            Error(code, message, sourceIdentity, context),
        ]);

    private static Canvas2DRendererResult UnexpectedFailure(
        string code,
        string message,
        Exception exception,
        string sourceIdentity) =>
        Failure(
            code,
            message,
            sourceIdentity,
            new KeyValuePair<string, string>(
                "ExceptionType",
                exception.GetType().FullName ?? exception.GetType().Name));

    private static Diagnostic Error(
        string code,
        string message,
        string sourceIdentity,
        params KeyValuePair<string, string>[] context) =>
        new(code, DiagnosticSeverity.Error, message, sourceIdentity, context);

    private static Diagnostic TextError(
        string code,
        string message,
        string sourceIdentity,
        Exception? exception = null)
    {
        var context = exception is null
            ? Array.Empty<KeyValuePair<string, string>>()
            :
            [
                new KeyValuePair<string, string>(
                    "ExceptionType",
                    exception.GetType().FullName ?? exception.GetType().Name),
            ];
        return new Diagnostic(
            code,
            DiagnosticSeverity.Error,
            message,
            sourceIdentity,
            context);
    }

    private static bool IsNonFatal(Exception exception) =>
        exception is not OutOfMemoryException and
        not StackOverflowException and
        not AccessViolationException;
}
