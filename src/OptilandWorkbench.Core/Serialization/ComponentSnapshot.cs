using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Phase;
using OptilandWorkbench.Core.Scattering;

namespace OptilandWorkbench.Core.Serialization;

public sealed record ComponentSnapshot(
    string Kind,
    Dictionary<string, double> Numbers,
    Dictionary<string, string> Text,
    Dictionary<string, ComponentSnapshot>? Children = null)
{
    public static ComponentSnapshot Empty(string kind) => new(kind, new Dictionary<string, double>(), new Dictionary<string, string>());
}

public static partial class ComponentSnapshotFactory
{
    public static ComponentSnapshot? FromApodization(IApodizationModel? apodization)
    {
        return apodization switch
        {
            null => null,
            ZemaxApodization zemax => ComponentSnapshot.Empty(zemax.Kind) with
            {
                Numbers = new Dictionary<string, double> { ["type"] = (int)zemax.Type, ["factor"] = zemax.Factor }
            },
            UniformApodization => ComponentSnapshot.Empty("uniform"),
            GaussianApodization gaussian => ComponentSnapshot.Empty("gaussian") with
            {
                Numbers = new Dictionary<string, double> { ["sigma"] = gaussian.Sigma }
            },
            CosineSquaredApodization cosine => ComponentSnapshot.Empty("cosine_squared") with
            {
                Numbers = new Dictionary<string, double> { ["radius"] = cosine.Radius }
            },
            HannApodization hann => ComponentSnapshot.Empty("hann") with
            {
                Numbers = new Dictionary<string, double> { ["diameter"] = hann.Diameter }
            },
            PolynomialApodization polynomial => ComponentSnapshot.Empty("polynomial") with
            {
                Numbers = new Dictionary<string, double>
                {
                    ["radius"] = polynomial.Radius,
                    ["power"] = polynomial.Power
                }
            },
            SuperGaussianApodization superGaussian => ComponentSnapshot.Empty("super_gaussian") with
            {
                Numbers = new Dictionary<string, double>
                {
                    ["width"] = superGaussian.Width,
                    ["exponent"] = superGaussian.Exponent
                }
            },
            TukeyApodization tukey => ComponentSnapshot.Empty("tukey") with
            {
                Numbers = new Dictionary<string, double>
                {
                    ["radius"] = tukey.Radius,
                    ["alpha"] = tukey.Alpha
                }
            },
            _ => ComponentSnapshot.Empty(apodization.Kind)
        };
    }

    public static IApodizationModel? ToApodization(ComponentSnapshot? snapshot)
    {
        return snapshot?.Kind switch
        {
            null => null,
            "zemax_pupil" => new ZemaxApodization(
                (ZemaxApodizationType)Get(snapshot.Numbers, "type", 0),
                Get(snapshot.Numbers, "factor", 0)),
            "uniform" => new UniformApodization(),
            "gaussian" => new GaussianApodization(Get(snapshot.Numbers, "sigma", 1)),
            "cosine_squared" => new CosineSquaredApodization(Get(snapshot.Numbers, "radius", 1)),
            "hann" => new HannApodization(Get(snapshot.Numbers, "diameter", 2)),
            "polynomial" => new PolynomialApodization(
                Get(snapshot.Numbers, "radius", 1),
                Get(snapshot.Numbers, "power", 1)),
            "super_gaussian" => new SuperGaussianApodization(
                Get(snapshot.Numbers, "width", 1),
                Get(snapshot.Numbers, "exponent", 2)),
            "tukey" => new TukeyApodization(
                Get(snapshot.Numbers, "radius", 1),
                Get(snapshot.Numbers, "alpha", 0.5)),
            _ => null
        };
    }

    public static ComponentSnapshot FromGeometry(IGeometry geometry)
    {
        return geometry switch
        {
            PlaneGeometry => ComponentSnapshot.Empty("plane"),
            PlaneGratingGeometry grating => new ComponentSnapshot("plane_grating", new Dictionary<string, double>
            {
                ["order"] = grating.GratingOrder,
                ["periodMicrometers"] = grating.GratingPeriodMicrometers,
                ["angleRadians"] = grating.GrooveOrientationAngleRadians
            }, new Dictionary<string, string>()),
            StandardGratingGeometry grating => new ComponentSnapshot("standard_grating", new Dictionary<string, double>
            {
                ["radius"] = grating.Base.Radius,
                ["conic"] = grating.Base.Conic,
                ["order"] = grating.GratingOrder,
                ["periodMicrometers"] = grating.GratingPeriodMicrometers,
                ["angleRadians"] = grating.GrooveOrientationAngleRadians
            }, new Dictionary<string, string>()),
            StandardGeometry standard => new ComponentSnapshot("standard", new Dictionary<string, double>
            {
                ["radius"] = standard.Radius,
                ["conic"] = standard.Conic
            }, new Dictionary<string, string>()),
            EvenAsphereGeometry even => new ComponentSnapshot("even_asphere", Coefficients(even.Coefficients, new Dictionary<string, double>
            {
                ["radius"] = even.Base.Radius,
                ["conic"] = even.Base.Conic
            }), new Dictionary<string, string>()),
            OddAsphereGeometry odd => new ComponentSnapshot("odd_asphere", Coefficients(odd.Coefficients, new Dictionary<string, double>
            {
                ["radius"] = odd.Base.Radius,
                ["conic"] = odd.Base.Conic
            }), new Dictionary<string, string>()),
            BiconicGeometry biconic => new ComponentSnapshot("biconic", new Dictionary<string, double>
            {
                ["radiusX"] = biconic.RadiusX,
                ["radiusY"] = biconic.RadiusY,
                ["conicX"] = biconic.ConicX,
                ["conicY"] = biconic.ConicY
            }, new Dictionary<string, string>()),
            SeparableBiconicGeometry biconic => new ComponentSnapshot("separable_biconic", new Dictionary<string, double>
            {
                ["radiusX"] = biconic.RadiusX,
                ["radiusY"] = biconic.RadiusY,
                ["conicX"] = biconic.ConicX,
                ["conicY"] = biconic.ConicY
            }, new Dictionary<string, string>()),
            ToroidalGeometry toroidal => new ComponentSnapshot("toroidal", new Dictionary<string, double>
            {
                ["tangentialRadius"] = toroidal.TangentialRadius,
                ["sagittalRadius"] = toroidal.SagittalRadius
            }, new Dictionary<string, string>()),
            PolynomialGeometry polynomial => new ComponentSnapshot("polynomial", polynomial.Coefficients.ToDictionary(
                item => $"c_{item.Key.X}_{item.Key.Y}",
                item => item.Value), new Dictionary<string, string>()),
            ChebyshevGeometry chebyshev => new ComponentSnapshot("chebyshev", PairCoefficients(
                chebyshev.Coefficients,
                new Dictionary<string, double>
                {
                    ["normalizationX"] = chebyshev.NormalizationX,
                    ["normalizationY"] = chebyshev.NormalizationY
                }), new Dictionary<string, string>()),
            ZernikeGeometry zernike => new ComponentSnapshot("zernike", PairCoefficients(
                zernike.Coefficients,
                new Dictionary<string, double> { ["pupilRadius"] = zernike.PupilRadius }), new Dictionary<string, string>()),
            ForbesQGeometry forbes => new ComponentSnapshot("forbes_q", Coefficients(forbes.QCoefficients, new Dictionary<string, double>
            {
                ["radius"] = forbes.Base.Radius,
                ["conic"] = forbes.Base.Conic,
                ["normalizationRadius"] = forbes.NormalizationRadius
            }, "q"), new Dictionary<string, string>()),
            OpaqueGeometryPayload opaque => opaque.Payload,
            _ => throw new NotSupportedException(
                $"Geometry '{geometry.Kind}' has no lossless component serializer and cannot be saved as an empty placeholder.")
        };
    }

    public static IGeometry ToGeometry(ComponentSnapshot? snapshot, double fallbackRadius, double fallbackConic)
    {
        if (snapshot is null)
        {
            return Math.Abs(fallbackRadius) < 1e-12 ? new PlaneGeometry() : new StandardGeometry(fallbackRadius, fallbackConic);
        }

        var n = snapshot.Numbers;
        return snapshot.Kind switch
        {
            "plane" => new PlaneGeometry(),
            "plane_grating" => new PlaneGratingGeometry(
                (int)Get(n, "order", 1),
                Get(n, "periodMicrometers", 1),
                Get(n, "angleRadians", 0)),
            "standard_grating" => new StandardGratingGeometry(
                Get(n, "radius", fallbackRadius),
                Get(n, "conic", fallbackConic),
                (int)Get(n, "order", 1),
                Get(n, "periodMicrometers", 1),
                Get(n, "angleRadians", 0)),
            "standard" => new StandardGeometry(Get(n, "radius", fallbackRadius), Get(n, "conic", fallbackConic)),
            "even_asphere" => new EvenAsphereGeometry(Get(n, "radius", fallbackRadius), Get(n, "conic", fallbackConic), ReadCoefficients(n)),
            "odd_asphere" => new OddAsphereGeometry(Get(n, "radius", fallbackRadius), Get(n, "conic", fallbackConic), ReadCoefficients(n)),
            "biconic" => new BiconicGeometry(Get(n, "radiusX", fallbackRadius), Get(n, "radiusY", fallbackRadius), Get(n, "conicX", 0), Get(n, "conicY", 0)),
            "separable_biconic" => new SeparableBiconicGeometry(Get(n, "radiusX", fallbackRadius), Get(n, "radiusY", fallbackRadius), Get(n, "conicX", 0), Get(n, "conicY", 0)),
            "toroidal" => new ToroidalGeometry(Get(n, "tangentialRadius", fallbackRadius), Get(n, "sagittalRadius", fallbackRadius)),
            "polynomial" => new PolynomialGeometry(ReadPolynomial(n)),
            "chebyshev" => new ChebyshevGeometry(ReadPairCoefficients(n), Get(n, "normalizationX", 1), Get(n, "normalizationY", 1)),
            "zernike" => new ZernikeGeometry(ReadPairCoefficients(n), Get(n, "pupilRadius", 1)),
            "forbes_q" => new ForbesQGeometry(Get(n, "radius", fallbackRadius), Get(n, "conic", fallbackConic), Get(n, "normalizationRadius", 1), ReadCoefficients(n, "q")),
            _ => new OpaqueGeometryPayload(snapshot)
        };
    }

    public static ComponentSnapshot FromMaterial(IMaterial material)
    {
        return material switch
        {
            AirMaterial => ComponentSnapshot.Empty("air"),
            CatalogGlassMaterial catalog => FromCatalogGlass(catalog),
            UnresolvedMaterial unresolved => new ComponentSnapshot("unresolved", new Dictionary<string, double>(),
                new Dictionary<string, string> { ["name"] = unresolved.Name, ["catalogs"] = unresolved.Catalogs }),
            ConstantIndexMaterial constant => new ComponentSnapshot("constant", new Dictionary<string, double>
            {
                ["index"] = constant.Index,
                ["extinction"] = constant.Extinction
            }, new Dictionary<string, string> { ["name"] = constant.Name }),
            CauchyMaterial cauchy => new ComponentSnapshot("cauchy", new Dictionary<string, double>
            {
                ["a"] = cauchy.A,
                ["b"] = cauchy.B,
                ["c"] = cauchy.C
            }, new Dictionary<string, string> { ["name"] = cauchy.Name }),
            SellmeierMaterial sellmeier => new ComponentSnapshot("sellmeier", Coefficients(sellmeier.B, new Dictionary<string, double>(), "b")
                .Concat(Coefficients(sellmeier.C, new Dictionary<string, double>(), "c"))
                .Concat(Coefficients(sellmeier.ExtinctionWavelengthsNanometers, new Dictionary<string, double>(), "kw"))
                .Concat(Coefficients(sellmeier.ExtinctionCoefficients, new Dictionary<string, double>(), "k"))
                .ToDictionary(item => item.Key, item => item.Value), new Dictionary<string, string> { ["name"] = sellmeier.Name }),
            PolynomialDispersionMaterial polynomial => new ComponentSnapshot(
                "polynomial_dispersion",
                Coefficients(polynomial.Coefficients, new Dictionary<string, double>())
                    .Concat(Coefficients(polynomial.ExtinctionWavelengthsNanometers, new Dictionary<string, double>(), "kw"))
                    .Concat(Coefficients(polynomial.ExtinctionCoefficients, new Dictionary<string, double>(), "k"))
                    .ToDictionary(item => item.Key, item => item.Value),
                new Dictionary<string, string> { ["name"] = polynomial.Name }),
            AbbeMaterial abbe => new ComponentSnapshot("abbe", new Dictionary<string, double>
            {
                ["nd"] = abbe.Nd,
                ["vd"] = abbe.Vd
            }, new Dictionary<string, string> { ["name"] = abbe.Name }),
            _ => new ComponentSnapshot("catalog", new Dictionary<string, double>(), new Dictionary<string, string> { ["name"] = material.Name })
        };
    }

    public static IMaterial ToMaterial(ComponentSnapshot? snapshot, string fallbackName, MaterialRegistry registry)
    {
        if (snapshot is null)
        {
            return registry.Resolve(fallbackName);
        }

        var name = snapshot.Text.TryGetValue("name", out var storedName) ? storedName : fallbackName;
        return snapshot.Kind switch
        {
            "air" => new AirMaterial(),
            "catalog_glass" => ToCatalogGlass(snapshot, name),
            "unresolved" => new UnresolvedMaterial(name, snapshot.Text.GetValueOrDefault("catalogs", "")),
            "constant" => new ConstantIndexMaterial(name, Get(snapshot.Numbers, "index", 1.5), Get(snapshot.Numbers, "extinction", 0)),
            "cauchy" => new CauchyMaterial(name, Get(snapshot.Numbers, "a", 1.5), Get(snapshot.Numbers, "b", 0), Get(snapshot.Numbers, "c", 0)),
            "sellmeier" => new SellmeierMaterial(
                name,
                ReadCoefficients(snapshot.Numbers, "b"),
                ReadCoefficients(snapshot.Numbers, "c"),
                extinctionWavelengthsNanometers: ReadCoefficients(snapshot.Numbers, "kw"),
                extinctionCoefficients: ReadCoefficients(snapshot.Numbers, "k")),
            "polynomial_dispersion" => new PolynomialDispersionMaterial(
                name,
                ReadCoefficients(snapshot.Numbers),
                extinctionWavelengthsNanometers: ReadCoefficients(snapshot.Numbers, "kw"),
                extinctionCoefficients: ReadCoefficients(snapshot.Numbers, "k")),
            "abbe" => new AbbeMaterial(name, Get(snapshot.Numbers, "nd", 1.5), Get(snapshot.Numbers, "vd", 50)),
            _ => registry.Resolve(name)
        };
    }

    public static ComponentSnapshot FromCoating(ICoatingModel coating)
    {
        if (coating is SimpleCoatingModel simple)
        {
            return new ComponentSnapshot("simple", new Dictionary<string, double>
            {
                ["transmittance"] = simple.Transmittance,
                ["reflectance"] = simple.Reflectance
            }, new Dictionary<string, string>());
        }

        if (coating is ApproximateTransmissionRippleCoating stack)
        {
            var numbers = new Dictionary<string, double> { ["count"] = stack.Layers.Count };
            var text = new Dictionary<string, string>();
            for (var index = 0; index < stack.Layers.Count; index++)
            {
                numbers[$"thickness_{index}"] = stack.Layers[index].ThicknessNanometers;
                text[$"material_{index}"] = stack.Layers[index].MaterialName;
            }

            return new ComponentSnapshot("approximate_transmission_ripple", numbers, text);
        }

        return ComponentSnapshot.Empty("none");
    }

    public static ICoatingModel ToCoating(ComponentSnapshot? snapshot)
    {
        if (snapshot?.Kind == "simple")
        {
            return new SimpleCoatingModel(
                Get(snapshot.Numbers, "transmittance", 1),
                Get(snapshot.Numbers, "reflectance", 0));
        }

        if (snapshot?.Kind is not ("thin_film_stack" or "approximate_transmission_ripple"))
        {
            return new NoneCoatingModel();
        }

        var count = (int)Get(snapshot.Numbers, "count", 0);
        var layers = Enumerable.Range(0, count)
            .Select(index => new ThinFilmLayer(
                snapshot.Text.TryGetValue($"material_{index}", out var material) ? material : "N-BK7",
                Get(snapshot.Numbers, $"thickness_{index}", 100)))
            .ToArray();
        return new ApproximateTransmissionRippleCoating(layers);
    }

    public static ComponentSnapshot FromInteraction(IInteractionModel interaction)
    {
        return interaction switch
        {
            RefractiveReflectiveInteractionModel model => new ComponentSnapshot(model.IsReflective ? "reflective" : "refractive", new Dictionary<string, double>(), new Dictionary<string, string>()),
            ThinLensInteractionModel thinLens => new ComponentSnapshot("thin_lens", new Dictionary<string, double>
            {
                ["focalLength"] = thinLens.FocalLength,
                ["isReflective"] = thinLens.IsReflective ? 1 : 0
            }, new Dictionary<string, string>()),
            DiffractiveInteractionModel diffractive => new ComponentSnapshot(
                "diffractive",
                DiffractiveNumbers(diffractive),
                new Dictionary<string, string>()),
            PhaseInteractionModel phase => new ComponentSnapshot(
                "phase",
                new Dictionary<string, double> { ["isReflective"] = phase.IsReflective ? 1 : 0 },
                new Dictionary<string, string>(),
                new Dictionary<string, ComponentSnapshot>
                {
                    ["profile"] = FromPhaseProfile(phase.Profile)
                }),
            _ => ComponentSnapshot.Empty(interaction.Kind)
        };
    }

    public static IInteractionModel ToInteraction(ComponentSnapshot? snapshot, bool isReflective)
    {
        return snapshot?.Kind switch
        {
            "reflective" => new RefractiveReflectiveInteractionModel(true),
            "refractive" => new RefractiveReflectiveInteractionModel(false),
            "thin_lens" => new ThinLensInteractionModel(
                Get(snapshot.Numbers, "focalLength", 50),
                Get(snapshot.Numbers, "isReflective", 0) != 0),
            "diffractive" when snapshot.Numbers.ContainsKey("grooveFrequency") =>
                new DiffractiveInteractionModel(
                    Get(snapshot.Numbers, "grooveFrequency", 1),
                    (int)Get(snapshot.Numbers, "order", 1)),
            "diffractive" => new DiffractiveInteractionModel(Get(snapshot.Numbers, "isReflective", 0) != 0),
            "phase" => new PhaseInteractionModel(
                snapshot.Children is not null
                    && snapshot.Children.TryGetValue("profile", out var profile)
                    ? ToPhaseProfile(profile)
                    : new ConstantPhaseProfile(),
                Get(snapshot.Numbers, "isReflective", 0) != 0),
            _ => new RefractiveReflectiveInteractionModel(isReflective)
        };
    }

    private static Dictionary<string, double> DiffractiveNumbers(DiffractiveInteractionModel model)
    {
        var numbers = new Dictionary<string, double>
        {
            ["isReflective"] = model.IsReflective ? 1 : 0
        };
        if (model.GrooveFrequencyLinesPerMillimeter is double frequency)
        {
            numbers["grooveFrequency"] = frequency;
            numbers["order"] = model.Order ?? 1;
        }

        return numbers;
    }

    private static ComponentSnapshot FromPhaseProfile(IPhaseProfile profile)
    {
        return profile switch
        {
            ConstantPhaseProfile constant => new ComponentSnapshot(
                "constant",
                new Dictionary<string, double> { ["phase"] = constant.PhaseValue },
                new Dictionary<string, string>()),
            LinearGratingPhaseProfile linear => new ComponentSnapshot(
                "linear_grating",
                new Dictionary<string, double>
                {
                    ["period"] = linear.Period,
                    ["angle"] = linear.Angle,
                    ["order"] = linear.Order,
                    ["efficiency"] = linear.Efficiency
                },
                new Dictionary<string, string>()),
            RadialPhaseProfile radial => new ComponentSnapshot(
                "radial",
                Coefficients(radial.Coefficients, new Dictionary<string, double>()),
                new Dictionary<string, string>()),
            GridPhaseProfile grid => new ComponentSnapshot(
                "grid",
                GridPhaseNumbers(grid),
                new Dictionary<string, string>()),
            PolynomialPhaseProfile polynomial => new ComponentSnapshot(
                "polynomial_phase",
                PairCoefficients(polynomial.Coefficients, new Dictionary<string, double>()),
                new Dictionary<string, string>()),
            _ => ComponentSnapshot.Empty(profile.Kind)
        };
    }

    private static IPhaseProfile ToPhaseProfile(ComponentSnapshot snapshot)
    {
        return snapshot.Kind switch
        {
            "constant" => new ConstantPhaseProfile(Get(snapshot.Numbers, "phase", 0)),
            "linear_grating" => new LinearGratingPhaseProfile(
                Get(snapshot.Numbers, "period", 1),
                Get(snapshot.Numbers, "angle", 0),
                (int)Get(snapshot.Numbers, "order", 1),
                Get(snapshot.Numbers, "efficiency", 1)),
            "radial" => new RadialPhaseProfile(ReadCoefficients(snapshot.Numbers)),
            "grid" => ReadGridPhaseProfile(snapshot.Numbers),
            "polynomial_phase" => new PolynomialPhaseProfile(ReadPairCoefficients(snapshot.Numbers)),
            _ => new ConstantPhaseProfile()
        };
    }

    public static ComponentSnapshot? FromAperture(IPhysicalAperture? aperture)
    {
        return aperture switch
        {
            null => null,
            CircularAperture circular => new ComponentSnapshot("circular", new Dictionary<string, double> { ["radius"] = circular.Radius }, new Dictionary<string, string>()),
            AnnularAperture annular => new ComponentSnapshot("annular", new Dictionary<string, double>
            {
                ["outerRadius"] = annular.OuterRadius,
                ["innerRadius"] = annular.InnerRadius
            }, new Dictionary<string, string>()),
            OffsetRadialAperture offset => new ComponentSnapshot("offset_radial", new Dictionary<string, double>
            {
                ["outerRadius"] = offset.OuterRadius,
                ["innerRadius"] = offset.InnerRadius,
                ["offsetX"] = offset.OffsetX,
                ["offsetY"] = offset.OffsetY
            }, new Dictionary<string, string>()),
            RectangularAperture rectangular => new ComponentSnapshot("rectangular", new Dictionary<string, double>
            {
                ["halfWidth"] = rectangular.HalfWidth,
                ["halfHeight"] = rectangular.HalfHeight,
                ["centerX"] = rectangular.CenterX,
                ["centerY"] = rectangular.CenterY
            }, new Dictionary<string, string>()),
            EllipticalAperture elliptical => new ComponentSnapshot("elliptical", new Dictionary<string, double>
            {
                ["semiAxisX"] = elliptical.SemiAxisX,
                ["semiAxisY"] = elliptical.SemiAxisY,
                ["offsetX"] = elliptical.OffsetX,
                ["offsetY"] = elliptical.OffsetY
            }, new Dictionary<string, string>()),
            FileAperture file => new ComponentSnapshot(
                "file",
                PolygonNumbers(file.Vertices, new Dictionary<string, double>
                {
                    ["skipHeader"] = file.SkipHeader
                }),
                file.Delimiter is null
                    ? new Dictionary<string, string> { ["filePath"] = file.FilePath }
                    : new Dictionary<string, string>
                    {
                        ["filePath"] = file.FilePath,
                        ["delimiter"] = file.Delimiter
                    }),
            PolygonAperture polygon => new ComponentSnapshot(
                "polygon",
                PolygonNumbers(polygon.Vertices, new Dictionary<string, double>()),
                new Dictionary<string, string>()),
            BooleanAperture boolean => new ComponentSnapshot(
                boolean.Kind,
                new Dictionary<string, double>(),
                new Dictionary<string, string>(),
                new Dictionary<string, ComponentSnapshot>
                {
                    ["left"] = FromAperture(boolean.Left)!,
                    ["right"] = FromAperture(boolean.Right)!
                }),
            _ => ComponentSnapshot.Empty(aperture.Kind)
        };
    }

    public static IPhysicalAperture? ToAperture(ComponentSnapshot? snapshot, double fallbackRadius)
    {
        return snapshot?.Kind switch
        {
            "circular" => new CircularAperture(Get(snapshot.Numbers, "radius", fallbackRadius)),
            "annular" => new AnnularAperture(
                Get(snapshot.Numbers, "outerRadius", fallbackRadius),
                Get(snapshot.Numbers, "innerRadius", 0)),
            "offset_radial" => new OffsetRadialAperture(
                Get(snapshot.Numbers, "outerRadius", fallbackRadius),
                Get(snapshot.Numbers, "innerRadius", 0),
                Get(snapshot.Numbers, "offsetX", 0),
                Get(snapshot.Numbers, "offsetY", 0)),
            "rectangular" => new RectangularAperture(
                Get(snapshot.Numbers, "halfWidth", fallbackRadius),
                Get(snapshot.Numbers, "halfHeight", fallbackRadius),
                Get(snapshot.Numbers, "centerX", 0),
                Get(snapshot.Numbers, "centerY", 0)),
            "elliptical" => new EllipticalAperture(
                Get(snapshot.Numbers, "semiAxisX", fallbackRadius),
                Get(snapshot.Numbers, "semiAxisY", fallbackRadius),
                Get(snapshot.Numbers, "offsetX", 0),
                Get(snapshot.Numbers, "offsetY", 0)),
            "polygon" => new PolygonAperture(ReadPolygonVertices(snapshot.Numbers)),
            "file" => new FileAperture(
                ReadPolygonVertices(snapshot.Numbers),
                snapshot.Text.TryGetValue("filePath", out var filePath) ? filePath : string.Empty,
                snapshot.Text.TryGetValue("delimiter", out var delimiter) ? delimiter : null,
                (int)Get(snapshot.Numbers, "skipHeader", 0)),
            "union" => ReadBooleanAperture(snapshot, (left, right) => new UnionAperture(left, right), fallbackRadius),
            "intersection" => ReadBooleanAperture(snapshot, (left, right) => new IntersectionAperture(left, right), fallbackRadius),
            "difference" => ReadBooleanAperture(snapshot, (left, right) => new DifferenceAperture(left, right), fallbackRadius),
            null => null,
            _ => new CircularAperture(fallbackRadius)
        };
    }

    public static ComponentSnapshot? FromScattering(IScatteringModel? scattering)
    {
        return scattering switch
        {
            null => null,
            MainRayScatterLossApproximation approximation => new ComponentSnapshot(
                "main_ray_scatter_loss_approximation",
                new Dictionary<string, double> { ["scatterFraction"] = approximation.ScatterFraction },
                new Dictionary<string, string>()),
            MeanMeasuredScatterLoss measured => FromMeasuredScatterLoss(measured),
            _ => ComponentSnapshot.Empty(scattering.Kind)
        };
    }

    public static IScatteringModel? ToScattering(ComponentSnapshot? snapshot)
    {
        return snapshot?.Kind switch
        {
            "lambertian" or "main_ray_scatter_loss_approximation" =>
                new MainRayScatterLossApproximation(Get(snapshot.Numbers, "scatterFraction", 0.02)),
            "measured_bsdf" or "mean_measured_scatter_loss" =>
                new MeanMeasuredScatterLoss(ReadMeasuredBsdfSamples(snapshot.Numbers)),
            _ => null
        };
    }

    private static ComponentSnapshot FromMeasuredScatterLoss(MeanMeasuredScatterLoss measured)
    {
        var numbers = new Dictionary<string, double>
        {
            ["sampleCount"] = measured.Samples.Count
        };
        for (var index = 0; index < measured.Samples.Count; index++)
        {
            numbers[$"angle{index}"] = measured.Samples[index].AngleDegrees;
            numbers[$"value{index}"] = measured.Samples[index].Value;
        }

        return new ComponentSnapshot("mean_measured_scatter_loss", numbers, new Dictionary<string, string>());
    }

    private static IReadOnlyList<(double AngleDegrees, double Value)> ReadMeasuredBsdfSamples(
        IReadOnlyDictionary<string, double> numbers)
    {
        var rawCount = Get(numbers, "sampleCount", 0);
        var requestedCount = double.IsFinite(rawCount) && rawCount > 0
            ? (int)Math.Min(rawCount, 1_000_000)
            : 0;
        var count = Math.Min(requestedCount, numbers.Count / 2);
        var samples = new List<(double AngleDegrees, double Value)>(count);
        for (var index = 0; index < count; index++)
        {
            if (numbers.TryGetValue($"angle{index}", out var angle)
                && numbers.TryGetValue($"value{index}", out var value))
            {
                samples.Add((angle, value));
            }
        }

        return samples;
    }

    private static double Get(IReadOnlyDictionary<string, double> values, string key, double fallback)
    {
        return values.TryGetValue(key, out var value) ? value : fallback;
    }

    private static Dictionary<string, double> PolygonNumbers(
        IReadOnlyList<(double X, double Y)> vertices,
        Dictionary<string, double> seed)
    {
        seed["vertexCount"] = vertices.Count;
        for (var index = 0; index < vertices.Count; index++)
        {
            seed[$"x{index}"] = vertices[index].X;
            seed[$"y{index}"] = vertices[index].Y;
        }

        return seed;
    }

    private static IReadOnlyList<(double X, double Y)> ReadPolygonVertices(
        IReadOnlyDictionary<string, double> numbers)
    {
        var count = Math.Max(0, (int)Get(numbers, "vertexCount", 0));
        return Enumerable.Range(0, count)
            .Select(index => (Get(numbers, $"x{index}", 0), Get(numbers, $"y{index}", 0)))
            .ToArray();
    }

    private static IPhysicalAperture ReadBooleanAperture(
        ComponentSnapshot snapshot,
        Func<IPhysicalAperture, IPhysicalAperture, IPhysicalAperture> factory,
        double fallbackRadius)
    {
        if (snapshot.Children is null
            || !snapshot.Children.TryGetValue("left", out var leftSnapshot)
            || !snapshot.Children.TryGetValue("right", out var rightSnapshot))
        {
            return new CircularAperture(fallbackRadius);
        }

        return factory(
            ToAperture(leftSnapshot, fallbackRadius)!,
            ToAperture(rightSnapshot, fallbackRadius)!);
    }

    private static Dictionary<string, double> GridPhaseNumbers(GridPhaseProfile grid)
    {
        var numbers = new Dictionary<string, double>
        {
            ["xCount"] = grid.XCoordinates.Count,
            ["yCount"] = grid.YCoordinates.Count
        };
        for (var x = 0; x < grid.XCoordinates.Count; x++)
        {
            numbers[$"x{x}"] = grid.XCoordinates[x];
        }

        for (var y = 0; y < grid.YCoordinates.Count; y++)
        {
            numbers[$"y{y}"] = grid.YCoordinates[y];
            for (var x = 0; x < grid.XCoordinates.Count; x++)
            {
                numbers[$"g{y}_{x}"] = grid.PhaseGrid[y, x];
            }
        }

        return numbers;
    }

    private static GridPhaseProfile ReadGridPhaseProfile(IReadOnlyDictionary<string, double> numbers)
    {
        var xCount = ReadCount(numbers, "xCount", 4, PhaseProfileLimits.MaximumGridAxisCount);
        var yCount = ReadCount(numbers, "yCount", 4, PhaseProfileLimits.MaximumGridAxisCount);
        if ((long)xCount * yCount > PhaseProfileLimits.MaximumGridCellCount)
        {
            throw new InvalidDataException(
                $"Phase grid must not exceed {PhaseProfileLimits.MaximumGridCellCount:N0} cells.");
        }

        var xCoordinates = Enumerable.Range(0, xCount)
            .Select(index => Get(numbers, $"x{index}", index))
            .ToArray();
        var yCoordinates = Enumerable.Range(0, yCount)
            .Select(index => Get(numbers, $"y{index}", index))
            .ToArray();
        var grid = new double[yCount, xCount];
        for (var y = 0; y < yCount; y++)
        {
            for (var x = 0; x < xCount; x++)
            {
                grid[y, x] = Get(numbers, $"g{y}_{x}", 0);
            }
        }

        return new GridPhaseProfile(xCoordinates, yCoordinates, grid);
    }

    private static int ReadCount(
        IReadOnlyDictionary<string, double> numbers,
        string key,
        int minimum,
        int maximum)
    {
        var value = Get(numbers, key, minimum);
        if (!double.IsFinite(value)
            || value != Math.Truncate(value)
            || value < minimum
            || value > maximum)
        {
            throw new InvalidDataException(
                $"Encoded collection count '{key}' must be an integer between {minimum:N0} and {maximum:N0}.");
        }

        return checked((int)value);
    }

    private static Dictionary<string, double> Coefficients(IReadOnlyList<double> coefficients, Dictionary<string, double> seed, string prefix = "c")
    {
        for (var index = 0; index < coefficients.Count; index++)
        {
            seed[$"{prefix}{index}"] = coefficients[index];
        }

        return seed;
    }

    private static IReadOnlyList<double> ReadCoefficients(IReadOnlyDictionary<string, double> values, string prefix = "c")
    {
        return values
            .Where(item => item.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && int.TryParse(item.Key[prefix.Length..], out _))
            .OrderBy(item => int.Parse(item.Key[prefix.Length..]))
            .Select(item => item.Value)
            .ToArray();
    }

    private static IReadOnlyDictionary<(int X, int Y), double> ReadPolynomial(IReadOnlyDictionary<string, double> values)
    {
        var result = new Dictionary<(int X, int Y), double>();
        foreach (var item in values)
        {
            var parts = item.Key.Split('_');
            if (parts.Length == 3 && parts[0] == "c" && int.TryParse(parts[1], out var x) && int.TryParse(parts[2], out var y))
            {
                result[(x, y)] = item.Value;
            }
        }

        return result;
    }

    private static Dictionary<string, double> PairCoefficients(IReadOnlyDictionary<(int X, int Y), double> coefficients, Dictionary<string, double> seed)
    {
        foreach (var item in coefficients)
        {
            seed[$"c_{item.Key.X}_{item.Key.Y}"] = item.Value;
        }

        return seed;
    }

    private static IReadOnlyDictionary<(int X, int Y), double> ReadPairCoefficients(IReadOnlyDictionary<string, double> values)
    {
        return ReadPolynomial(values);
    }
}
