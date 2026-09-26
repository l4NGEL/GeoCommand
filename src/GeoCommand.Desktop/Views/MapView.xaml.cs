using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Windows.Threading;
using GeoCommand.Contracts;
using GeoCommand.Desktop.Converters;
using GeoCommand.Desktop.ViewModels;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using NetTopologySuite.Geometries;
using MapsuiColor = Mapsui.Styles.Color;
using MapsuiBrush = Mapsui.Styles.Brush;

namespace GeoCommand.Desktop.Views;

/// <summary>
/// Mapsui tabanlı harita. Yalnızca görünüm işini yapar: ViewModel'deki koleksiyonları katmanlara çizer ve
/// haritaya tıklamayı WGS84 enlem/boylama çevirip ViewModel'e iletir. Harita EPSG:3857 (Web Mercator) ile
/// çizilir; API ile alışveriş her zaman EPSG:4326 (enlem/boylam) iledir.
/// </summary>
public partial class MapView : UserControl
{
    private readonly Mapsui.Map _map = new();
    private readonly MemoryLayer _zones = new("Bölgeler");
    private readonly MemoryLayer _history = new("Geçmiş iz");
    private readonly MemoryLayer _draft = new("Çizilen bölge");
    private readonly MemoryLayer _vehicles = new("Araçlar");
    private readonly DispatcherTimer _redrawTimer;
    private bool _vehiclesDirty;
    private MainViewModel? _vm;

    public MapView()
    {
        InitializeComponent();
        _zones.Style = null;
        _history.Style = null;
        _draft.Style = null;
        _vehicles.Style = null;

        // Canlı güncellemelerde her özellik değişiminde yeniden çizmek yerine kısa aralıklarla toplu çiz.
        _redrawTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, (_, _) => FlushVehicles(), Dispatcher);
        DataContextChanged += (_, _) => Attach(DataContext as MainViewModel);
    }

    public void Configure(MapOptions options)
    {
        if (options.UseOnlineTiles)
        {
            // OSM karo kullanım politikası: uygulamayı tanımlayan User-Agent ve görünür atıf zorunlu; yoğun kullanım için uygun değildir.
            _map.Layers.Add(OpenStreetMap.CreateTileLayer(options.UserAgent));
        }
        else
        {
            OfflineBadge.Visibility = Visibility.Visible;
            Attribution.Visibility = Visibility.Collapsed;
        }

        _map.Layers.Add(_zones);
        _map.Layers.Add(_history);
        _map.Layers.Add(_draft);
        _map.Layers.Add(_vehicles);
        _map.Tapped += OnMapTapped;
        _map.PointerMoved += OnPointerMoved;
        MapControl.Map = _map;

        var (x, y) = SphericalMercator.FromLonLat(options.CenterLongitude, options.CenterLatitude);
        _map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), options.Resolution, 0, null!);
        _redrawTimer.Start();
    }

    private void Attach(MainViewModel? vm)
    {
        if (_vm is not null || vm is null) return;
        _vm = vm;

        vm.Vehicles.CollectionChanged += (_, e) =>
        {
            foreach (var item in e.NewItems?.OfType<VehicleItemViewModel>() ?? []) item.PropertyChanged += OnVehicleChanged;
            _vehiclesDirty = true;
        };
        foreach (var v in vm.Vehicles) v.PropertyChanged += OnVehicleChanged;

        vm.Zones.CollectionChanged += (_, _) => DrawZones();
        vm.DraftVertices.CollectionChanged += (_, _) => DrawDraft();
        vm.HistoryChanged += DrawHistory;
        vm.FocusVehicleRequested += Focus;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedVehicle)) _vehiclesDirty = true;
            if (e.PropertyName == nameof(MainViewModel.IsDrawingZone)) DrawDraft();
        };
        DrawZones();
        _vehiclesDirty = true;
    }

    private void OnVehicleChanged(object? sender, PropertyChangedEventArgs e) => _vehiclesDirty = true;

    // ---- Etkileşim ----

    private void OnMapTapped(object? sender, MapEventArgs e)
    {
        if (_vm is null) return;

        if (_vm.IsDrawingZone)
        {
            var (lon, lat) = SphericalMercator.ToLonLat(e.WorldPosition.X, e.WorldPosition.Y);
            _vm.AddDraftVertex(lat, lon);
            e.Handled = true;
            return;
        }

        var info = e.GetMapInfo([_vehicles]);
        if (info?.Feature?["id"] is Guid id)
        {
            _vm.SelectedVehicle = _vm.State.FindVehicle(id);
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object? sender, MapEventArgs e)
    {
        if (_vm is null) return;
        var (lon, lat) = SphericalMercator.ToLonLat(e.WorldPosition.X, e.WorldPosition.Y);
        _vm.CursorPositionText = string.Create(CultureInfo.InvariantCulture, $"İmleç: {lat:0.00000}, {lon:0.00000}");
    }

    private void Focus(VehicleItemViewModel vehicle)
    {
        if (!vehicle.HasPosition) return;
        var (x, y) = SphericalMercator.FromLonLat(vehicle.Longitude!.Value, vehicle.Latitude!.Value);
        _map.Navigator.CenterOn(new MPoint(x, y), 400, null!);
    }

    private void OnAttributionClick(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    // ---- Çizim ----

    private static Coordinate ToMercator(double lat, double lon)
    {
        var (x, y) = SphericalMercator.FromLonLat(lon, lat);
        return new Coordinate(x, y);
    }

    private static MapsuiColor ToMapsui(System.Windows.Media.Color c, int alpha = 255) => MapsuiColor.FromArgb(alpha, c.R, c.G, c.B);

    private void FlushVehicles()
    {
        if (!_vehiclesDirty || _vm is null) return;
        _vehiclesDirty = false;

        var features = new List<IFeature>();
        foreach (var v in _vm.Vehicles.Where(v => v.HasPosition))
        {
            var (x, y) = SphericalMercator.FromLonLat(v.Longitude!.Value, v.Latitude!.Value);
            var selected = _vm.SelectedVehicle?.Id == v.Id;
            var color = ToMapsui(Palette.ForStatus(v.Status));
            var feature = new PointFeature(x, y) { ["id"] = v.Id };

            if (selected)
                feature.Styles.Add(new SymbolStyle
                {
                    SymbolType = SymbolType.Ellipse, SymbolScale = 1.1,
                    Fill = new MapsuiBrush(MapsuiColor.FromArgb(60, 47, 111, 214)),
                    Outline = new Pen(MapsuiColor.FromArgb(255, 47, 111, 214), 2)
                });
            feature.Styles.Add(new SymbolStyle
            {
                // Üçgen aracın yönünü gösterir.
                SymbolType = SymbolType.Triangle, SymbolScale = selected ? 0.75 : 0.6,
                SymbolRotation = v.HeadingDegrees, RotateWithMap = true,
                Fill = new MapsuiBrush(color), Outline = new Pen(MapsuiColor.White, 1.5)
            });
            feature.Styles.Add(new LabelStyle
            {
                Text = v.Callsign,
                Offset = new Offset(0, -22),
                Font = new Font { Size = 11, Bold = selected },
                ForeColor = MapsuiColor.FromArgb(255, 31, 42, 55),
                BackColor = new MapsuiBrush(MapsuiColor.FromArgb(210, 255, 255, 255)),
                Halo = null!
            });
            features.Add(feature);
        }
        Replace(_vehicles, features);
    }

    private void DrawZones()
    {
        if (_vm is null) return;
        var features = new List<IFeature>();
        foreach (var zone in _vm.Zones)
        {
            var ring = zone.Vertices.Select(p => ToMercator(p.Latitude, p.Longitude)).ToList();
            if (ring.Count < 3) continue;
            ring.Add(ring[0]);
            var feature = new GeometryFeature(new Polygon(new LinearRing(ring.ToArray()))) { ["id"] = zone.Id };
            feature.Styles.Add(new VectorStyle
            {
                Fill = new MapsuiBrush(MapsuiColor.FromArgb(45, 217, 108, 30)),
                Outline = new Pen(MapsuiColor.FromArgb(230, 217, 108, 30), 2)
            });
            feature.Styles.Add(new LabelStyle
            {
                Text = zone.Name, ForeColor = MapsuiColor.FromArgb(255, 140, 60, 10),
                BackColor = new MapsuiBrush(MapsuiColor.FromArgb(200, 255, 255, 255)), Halo = null!
            });
            features.Add(feature);
        }
        Replace(_zones, features);
    }

    private void DrawDraft()
    {
        if (_vm is null) return;
        var features = new List<IFeature>();
        var points = _vm.DraftVertices.Select(p => ToMercator(p.Latitude, p.Longitude)).ToList();
        if (_vm.IsDrawingZone && points.Count >= 2)
        {
            var line = new LineString([.. points, points.Count >= 3 ? points[0] : points[^1]]);
            var outline = new GeometryFeature(line);
            outline.Styles.Add(new VectorStyle { Line = new Pen(MapsuiColor.FromArgb(255, 47, 111, 214), 2) { PenStyle = PenStyle.Dash } });
            features.Add(outline);
        }
        foreach (var p in _vm.IsDrawingZone ? points : [])
        {
            var vertex = new PointFeature(p.X, p.Y);
            vertex.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Rectangle, SymbolScale = 0.3,
                Fill = new MapsuiBrush(MapsuiColor.White), Outline = new Pen(MapsuiColor.FromArgb(255, 47, 111, 214), 2)
            });
            features.Add(vertex);
        }
        Replace(_draft, features);
    }

    private void DrawHistory()
    {
        if (_vm is null) return;
        var features = new List<IFeature>();
        var points = _vm.ShowHistoryOnMap ? _vm.HistoryPoints.Select(p => ToMercator(p.Latitude, p.Longitude)).ToList() : [];
        if (points.Count >= 2)
        {
            var track = new GeometryFeature(new LineString(points.ToArray()));
            track.Styles.Add(new VectorStyle { Line = new Pen(MapsuiColor.FromArgb(220, 123, 44, 191), 3) });
            features.Add(track);
        }
        if (points.Count >= 1)
        {
            var start = new PointFeature(points[0].X, points[0].Y);
            start.Styles.Add(new SymbolStyle { SymbolScale = 0.35, Fill = new MapsuiBrush(MapsuiColor.FromArgb(255, 123, 44, 191)) });
            start.Styles.Add(new LabelStyle { Text = "başlangıç", Offset = new Offset(0, 14), Halo = null! });
            features.Add(start);
        }
        Replace(_history, features);
    }

    private void Replace(MemoryLayer layer, List<IFeature> features)
    {
        layer.Features = features;
        layer.FeaturesWereModified();
        layer.DataHasChanged();
        _map.RefreshGraphics();
    }
}
