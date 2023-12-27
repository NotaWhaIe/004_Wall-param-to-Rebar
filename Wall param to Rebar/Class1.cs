using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace WallParamToRebar.RevitCommands
{
    [Autodesk.Revit.Attributes.TransactionAttribute(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class Test : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            List<Wall> walls = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>().ToList();
            List<Rebar> rebars = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>().ToList();

            // Кэширование геометрии стен и параметра "Орг.УровеньРазмещения"
            Dictionary<ElementId, Solid> wallSolids = new Dictionary<ElementId, Solid>();
            Dictionary<ElementId, ElementId> wallLevelIds = new Dictionary<ElementId, ElementId>();

            foreach (Wall wall in walls)
            {
                GeometryElement wallGeometryElement = wall.get_Geometry(new Options());
                Parameter wallLevelParam = wall.LookupParameter("Орг.УровеньРазмещения");
                ElementId wallLevelId = null;

                if (wallLevelParam != null && wallLevelParam.StorageType == StorageType.ElementId)
                {
                    wallLevelId = wallLevelParam.AsElementId();
                }

                foreach (GeometryObject geoObject in wallGeometryElement)
                {
                    if (geoObject is Solid wallSolid && wallSolid.Volume > 0)
                    {
                        wallSolids[wall.Id] = wallSolid;
                        if (wallLevelId != null)
                        {
                            wallLevelIds[wall.Id] = wallLevelId;
                        }
                    }
                }
            }

            using (Transaction gt = new Transaction(doc, "03_DarkMagic: WallId->RebarHostId"))
            {
                gt.Start();

                foreach (Rebar rebar in rebars)
                {
                    IList<Curve> centerlineCurves = rebar.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
                    if (centerlineCurves.Any())
                    {
                        XYZ centerPoint = GetCenterPoint(centerlineCurves.First());
                        Solid rebarCenterSolid = CreateSphereByPoint(centerPoint);

                        foreach (KeyValuePair<ElementId, Solid> pair in wallSolids)
                        {
                            if (DoSolidsIntersect(rebarCenterSolid, pair.Value))
                            {
                                //rebar.SetHostId(doc, pair.Key);/* UPD в группах не работает. Нужно разгруппировать и сгруппировать заново*/
                                if (wallLevelIds.TryGetValue(pair.Key, out ElementId wallLevelId))
                                {
                                    Parameter rebarLevelParam = rebar.LookupParameter("Орг.УровеньРазмещения");
                                    if (rebarLevelParam != null)
                                    {
                                        rebarLevelParam.Set(wallLevelId);
                                    }
                                }
                                break;
                            }
                        }
                    }
                }
                gt.Commit();
            }

            stopwatch.Stop();
            TimeSpan elapsedTime = stopwatch.Elapsed;
            TaskDialog.Show("Время работы", elapsedTime.TotalSeconds.ToString() + " сек.");

            return Result.Succeeded;
        }

        private XYZ GetCenterPoint(Curve curve)
        {
            return (curve.GetEndPoint(0) + curve.GetEndPoint(1)) / 2;
        }

        private static Solid CreateSphereByPoint(XYZ center)
        {
            List<Curve> profile = new List<Curve>();

            double radius = 250 / 304.8;
            XYZ profilePlus = center + new XYZ(0, radius, 0);
            XYZ profileMinus = center - new XYZ(0, radius, 0);

            profile.Add(Line.CreateBound(profilePlus, profileMinus));
            profile.Add(Arc.Create(profileMinus, profilePlus, center + new XYZ(radius, 0, 0)));

            CurveLoop curveLoop = CurveLoop.Create(profile);
            SolidOptions options = new SolidOptions(ElementId.InvalidElementId, ElementId.InvalidElementId);

            Frame frame = new Frame(center, XYZ.BasisX, -XYZ.BasisZ, XYZ.BasisY);
            if (Frame.CanDefineRevitGeometry(frame))
            {
                Solid sphere = GeometryCreationUtilities.CreateRevolvedGeometry(frame, new CurveLoop[] { curveLoop }, 0, 2 * Math.PI, options);
                return sphere;
            }
            else
            {
                return null;
            }
        }

        private static bool DoSolidsIntersect(Solid solid1, Solid solid2)
        {
            if (solid1 == null || solid2 == null)
                return false;

            try
            {
                Solid intersectSolid = BooleanOperationsUtils.ExecuteBooleanOperation(solid1, solid2, BooleanOperationsType.Intersect);
                return intersectSolid != null && intersectSolid.Volume > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
