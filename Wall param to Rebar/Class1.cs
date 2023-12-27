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
        // Executes the command to transfer parameters from walls to rebars
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();


            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            List<Wall> walls = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>().ToList();
            List<Rebar> rebars = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>().ToList();

            using (Transaction gt = new Transaction(doc, "00_DarkMagic: WallId->RebarHostId"))
            {
                gt.Start();

                foreach (Rebar rebar in rebars)
                {
                    IList<Curve> centerlineCurves = rebar.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
                    if (centerlineCurves.Any())
                    {
                        XYZ centerPoint = GetCenterPoint(centerlineCurves.First());
                        Solid rebarCenterSolid = CreateSphereByPoint(centerPoint);

                        // Поиск первой пересекающейся стены
                        ElementId hostWallId = FindFirstIntersectingWall(doc, rebarCenterSolid, walls);

                        // Устанавливаем HostId только если найдено пересечение
                        if (hostWallId != ElementId.InvalidElementId)
                        {
                            rebar.SetHostId(doc, hostWallId);
                        }
                    }
                }
                gt.Commit();
            }


            /////Более сложный и долгий вариант в 2 раза
            //using (Transaction gt = new Transaction(doc, "01_DarkMagic: WallId->RebarHostId"))
            //{
            //    gt.Start();

            //    foreach (Rebar rebar in rebars)
            //    {
            //        IList<Curve> centerlineCurves = rebar.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
            //        if (centerlineCurves.Any())
            //        {
            //            XYZ centerPoint = GetCenterPoint(centerlineCurves.First());
            //            Solid rebarCenterSolid = CreateSphereByPoint(centerPoint);

            //            // Проверка пересечения сферы с каждой стеной
            //            foreach (Wall wall in walls)
            //            {
            //                GeometryElement wallGeometryElement = wall.get_Geometry(new Options());
            //                foreach (GeometryObject geoObject in wallGeometryElement)
            //                {
            //                    Solid wallSolid = geoObject as Solid;
            //                    if (wallSolid != null && DoSolidsIntersect(rebarCenterSolid, wallSolid))
            //                    {
            //                        // Найдено пересечение сферы стержня и стены
            //                        rebar.SetHostId(doc, wall.Id);
            //                        break; // Прерываем цикл, так как найдено пересечение
            //                    }
            //                }
            //            }
            //        }
            //    }
            //    gt.Commit();
            //}

            stopwatch.Stop();
            TimeSpan elapsedTime = stopwatch.Elapsed;
            TaskDialog.Show("Время работы", elapsedTime.TotalSeconds.ToString() + " сек.");

            return Result.Succeeded;
        }


        public static Solid CreateSphereByPoint(XYZ center)
        {
            List<Curve> profile = new List<Curve>();

            // Установка радиуса сферы
            double radius = 3;
            //double radius = 250/304.8;
            //double radius = 0.2;
            XYZ profilePlus = center + new XYZ(0, radius, 0);
            XYZ profileMinus = center - new XYZ(0, radius, 0);

            // Создание профиля для революции
            profile.Add(Line.CreateBound(profilePlus, profileMinus));
            profile.Add(Arc.Create(profileMinus, profilePlus, center + new XYZ(radius, 0, 0)));

            CurveLoop curveLoop = CurveLoop.Create(profile);
            SolidOptions options = new SolidOptions(ElementId.InvalidElementId, ElementId.InvalidElementId);

            Frame frame = new Frame(center, XYZ.BasisX, -XYZ.BasisZ, XYZ.BasisY);
            if (Frame.CanDefineRevitGeometry(frame))
            {
                // Создание сферы путем революции профиля
                Solid sphere = GeometryCreationUtilities.CreateRevolvedGeometry(frame, new CurveLoop[] { curveLoop }, 0, 2 * Math.PI, options);
                return sphere;
            }
            else
            {
                return null;
            }
        }


        private XYZ GetCenterPoint(Curve curve)
        {
            return (curve.GetEndPoint(0) + curve.GetEndPoint(1)) / 2;
        }

        public static ElementId FindFirstIntersectingWall(Document doc, Solid solid, List<Wall> walls)
        {
            foreach (Wall wall in walls)
            {
                if (wall != null)
                {
                    GeometryElement wallGeometryElement = wall.get_Geometry(new Options());
                    foreach (GeometryObject geoObject in wallGeometryElement)
                    {
                        Solid wallSolid = geoObject as Solid;
                        if (wallSolid != null && DoSolidsIntersect(solid, wallSolid))
                        {
                            return wall.Id; // Возвращаем ID стены, если есть пересечение
                        }
                    }
                }
            }
            return ElementId.InvalidElementId; // Если пересечений не найдено
        }

        private static bool DoSolidsIntersect(Solid solid1, Solid solid2)
        {
            // Проверка на нулевые ссылки
            if (solid1 == null || solid2 == null)
                return false;

            // Попытка вычисления пересечения солидов
            try
            {
                Solid intersectSolid = BooleanOperationsUtils.ExecuteBooleanOperation(solid1, solid2, BooleanOperationsType.Intersect);
                if (intersectSolid != null && intersectSolid.Volume > 0)
                    return true; // Есть пересечение
            }
            catch
            {
                // В случае ошибки
            }
            return false; // Пересечений нет
        }
    }
}
