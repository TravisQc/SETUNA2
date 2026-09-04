using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SETUNA.Main.KeyItems;
using SETUNA.Main.Localization;
using SETUNA.Main.Style;
using SETUNA.Main.StyleItems;

namespace SETUNA.Main.Option
{
    // Token: 0x0200003C RID: 60
    public class SetunaOption : ICloneable
    {
        // Token: 0x06000241 RID: 577 RVA: 0x0000BC18 File Offset: 0x00009E18
        public static SetunaOption GetDefaultOption()
        {
            var setunaOption = new SetunaOption();
            var num = 1;
            setunaOption.Setuna.AppType = SetunaOption.SetunaOptionData.ApplicationType.ApplicationMode;
            setunaOption.Setuna.ShowMainWindow = true;
            setunaOption.Setuna.DupType = SetunaOption.SetunaOptionData.OpeningType.Normal;
            setunaOption.Setuna.DustBoxCapacity = 5;
            setunaOption.Setuna.DustBoxEnable = true;
            setunaOption.Setuna.SelectAreaTransparent = 80;
            setunaOption.Setuna.SelectBackColorR = Color.DarkBlue.R;
            setunaOption.Setuna.SelectBackColorG = Color.DarkBlue.G;
            setunaOption.Setuna.SelectBackColorB = Color.DarkBlue.B;
            setunaOption.Setuna.SelectLineColorR = Color.Blue.R;
            setunaOption.Setuna.SelectLineColorG = Color.Blue.G;
            setunaOption.Setuna.SelectLineColorB = Color.Blue.B;
            setunaOption.Setuna.ShowSplashWindow = true;
            setunaOption.Setuna.SelectLineSolid = false;
            setunaOption.ScrapHotKeyDatas = new Keys[(int)HotKeyID.__Count__] { Keys.Control | Keys.D1, Keys.Control | Keys.D2 };
            setunaOption.ScrapHotKeyEnable = true;
            setunaOption.Scrap.InactiveAlphaChange = true;
            setunaOption.Scrap.InactiveAlphaValue = 10;
            setunaOption.Scrap.MouseOverAlphaChange = true;
            setunaOption.Scrap.MouseOverAlphaValue = 90;


            setunaOption.Setuna.TopMostEnabled = false;
            setunaOption.Setuna.CursorEnabled = false;
            setunaOption.Setuna.FullscreenCursor = false;
            setunaOption.Setuna.MagnifierEnabled = false;
            setunaOption.Setuna.FullscreenCursorSolid = true;
            setunaOption.Setuna.FullscreenCursorColorR = Color.Orange.R;
            setunaOption.Setuna.FullscreenCursorColorG = Color.Orange.G;
            setunaOption.Setuna.FullscreenCursorColorB = Color.Orange.B;
            setunaOption.Setuna.BackgroundTransparentEnabled = false;

            // 显式设为「跟随系统」而不是留成 null：两者解析结果相同，但 XmlSerializer
            // 会整个省略值为 null 的字符串元素，新写出的配置里就看不到这一项。写成空串
            // 使元素出现在配置文件里，手工编辑配置的用户能看到有这个设置存在。
            setunaOption.Setuna.Language = Localization.AppLanguages.AutoValue;


            var cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.Copy")
            };
            cstyle.AddStyle(new CCopyStyleItem
            {
                CopyFromSource = true
            });
            cstyle.AddKeyItem((Keys)131139);
            var styleID = cstyle.StyleID;
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.CopyWithBorder")
            };
            cstyle.AddStyle(new CCopyStyleItem
            {
                CopyFromSource = false
            });
            cstyle.AddKeyItem((Keys)196675);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.Cut")
            };
            cstyle.AddStyle(new CCopyStyleItem
            {
                CopyFromSource = true
            });
            cstyle.AddStyle(new CCloseStyleItem());
            cstyle.AddKeyItem((Keys)131160);
            var styleID2 = cstyle.StyleID;
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.CutWithBorder")
            };
            cstyle.AddStyle(new CCopyStyleItem
            {
                CopyFromSource = false
            });
            cstyle.AddStyle(new CCloseStyleItem());
            cstyle.AddKeyItem((Keys)196696);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.Paste")
            };
            var newCi = new CPasteStyleItem();
            cstyle.AddStyle(newCi);
            cstyle.AddKeyItem((Keys)131158);
            var styleID3 = cstyle.StyleID;
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.Save")
            };
            cstyle.AddStyle(new CImagePngStyleItem
            {
                Quality = 100,
                ShowPreview = true,
                FileNameType = CImageStyleItem.EnumFileName.SaveAs,
                HaveMargin = false
            });
            cstyle.AddKeyItem((Keys)131155);
            var styleID4 = cstyle.StyleID;
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.SaveWithBorder")
            };
            cstyle.AddStyle(new CImagePngStyleItem
            {
                Quality = 100,
                ShowPreview = true,
                FileNameType = CImageStyleItem.EnumFileName.SaveAs,
                HaveMargin = true
            });
            cstyle.AddKeyItem((Keys)196691);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.Rotate90")
            };
            cstyle.AddStyle(new CRotateStyleItem
            {
                Rotate = 90
            });
            cstyle.AddKeyItem(Keys.R);
            var styleID5 = cstyle.StyleID;
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.FlipVertical")
            };
            cstyle.AddStyle(new CRotateStyleItem
            {
                VerticalReflection = true
            });
            cstyle.AddKeyItem((Keys)262230);
            var styleID6 = cstyle.StyleID;
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.FlipHorizontal")
            };
            cstyle.AddStyle(new CRotateStyleItem
            {
                HorizonReflection = true
            });
            cstyle.AddKeyItem((Keys)262216);
            var styleID7 = cstyle.StyleID;
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.Enlarge")
            };
            cstyle.AddStyle(new CScaleStyleItem
            {
                Value = 10,
                SetType = CScaleStyleItem.ScaleSetType.Increment
            });
            cstyle.AddKeyItem(Keys.RButton | Keys.MButton | Keys.Space | Keys.Alt);
            var styleID8 = cstyle.StyleID;
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.EnlargeFine")
            };
            cstyle.AddStyle(new CScaleStyleItem
            {
                Value = 1,
                SetType = CScaleStyleItem.ScaleSetType.Increment
            });
            cstyle.AddKeyItem(Keys.RButton | Keys.MButton | Keys.Space | Keys.Shift | Keys.Alt);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.Shrink")
            };
            cstyle.AddStyle(new CScaleStyleItem
            {
                Value = -10,
                SetType = CScaleStyleItem.ScaleSetType.Increment
            });
            cstyle.AddKeyItem(Keys.Back | Keys.Space | Keys.Alt);
            var styleID9 = cstyle.StyleID;
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.ShrinkFine")
            };
            cstyle.AddStyle(new CScaleStyleItem
            {
                Value = -1,
                SetType = CScaleStyleItem.ScaleSetType.Increment
            });
            cstyle.AddKeyItem(Keys.Back | Keys.Space | Keys.Shift | Keys.Alt);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.OpacityUp")
            };
            cstyle.AddStyle(new COpacityStyleItem
            {
                Absolute = false,
                Opacity = 10
            });
            cstyle.AddKeyItem(Keys.LButton | Keys.MButton | Keys.Space | Keys.Alt);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.OpacityUpFine")
            };
            cstyle.AddStyle(new COpacityStyleItem
            {
                Absolute = false,
                Opacity = 1
            });
            cstyle.AddKeyItem(Keys.LButton | Keys.MButton | Keys.Space | Keys.Shift | Keys.Alt);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.OpacityDown")
            };
            cstyle.AddStyle(new COpacityStyleItem
            {
                Absolute = false,
                Opacity = -10
            });
            cstyle.AddKeyItem(Keys.LButton | Keys.RButton | Keys.MButton | Keys.Space | Keys.Alt);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.OpacityDownFine")
            };
            cstyle.AddStyle(new COpacityStyleItem
            {
                Absolute = false,
                Opacity = -1
            });
            cstyle.AddKeyItem(Keys.LButton | Keys.RButton | Keys.MButton | Keys.Space | Keys.Shift | Keys.Alt);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.MoveUp")
            };
            cstyle.AddStyle(new CMoveStyleItem
            {
                Top = -50
            });
            cstyle.AddKeyItem(Keys.RButton | Keys.MButton | Keys.Space | Keys.Shift);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.MoveUpFine")
            };
            cstyle.AddStyle(new CMoveStyleItem
            {
                Top = -1
            });
            cstyle.AddKeyItem(Keys.Up);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.MoveDown")
            };
            cstyle.AddStyle(new CMoveStyleItem
            {
                Top = 50
            });
            cstyle.AddKeyItem(Keys.Back | Keys.Space | Keys.Shift);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.MoveDownFine")
            };
            cstyle.AddStyle(new CMoveStyleItem
            {
                Top = 1
            });
            cstyle.AddKeyItem(Keys.Down);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.MoveLeft")
            };
            cstyle.AddStyle(new CMoveStyleItem
            {
                Left = -50
            });
            cstyle.AddKeyItem(Keys.LButton | Keys.MButton | Keys.Space | Keys.Shift);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.MoveLeftFine")
            };
            cstyle.AddStyle(new CMoveStyleItem
            {
                Left = -1
            });
            cstyle.AddKeyItem(Keys.Left);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.MoveRight")
            };
            cstyle.AddStyle(new CMoveStyleItem
            {
                Left = 50
            });
            cstyle.AddKeyItem(Keys.LButton | Keys.RButton | Keys.MButton | Keys.Space | Keys.Shift);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.MoveRightFine")
            };
            cstyle.AddStyle(new CMoveStyleItem
            {
                Left = 1
            });
            cstyle.AddKeyItem(Keys.Right);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.Scale50")
            };
            cstyle.AddStyle(new CScaleStyleItem
            {
                SetType = CScaleStyleItem.ScaleSetType.Fixed,
                Value = 50
            });
            cstyle.AddKeyItem(Keys.D5);
            var styleID10 = cstyle.StyleID;
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.Scale60")
            };
            cstyle.AddStyle(new CScaleStyleItem
            {
                SetType = CScaleStyleItem.ScaleSetType.Fixed,
                Value = 60
            });
            cstyle.AddKeyItem(Keys.D6);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.Scale70")
            };
            cstyle.AddStyle(new CScaleStyleItem
            {
                SetType = CScaleStyleItem.ScaleSetType.Fixed,
                Value = 70
            });
            cstyle.AddKeyItem(Keys.D7);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.Scale80")
            };
            cstyle.AddStyle(new CScaleStyleItem
            {
                SetType = CScaleStyleItem.ScaleSetType.Fixed,
                Value = 80
            });
            cstyle.AddKeyItem(Keys.D8);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.Scale90")
            };
            cstyle.AddStyle(new CScaleStyleItem
            {
                SetType = CScaleStyleItem.ScaleSetType.Fixed,
                Value = 90
            });
            cstyle.AddKeyItem(Keys.D9);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.Scale100")
            };
            cstyle.AddStyle(new CScaleStyleItem
            {
                SetType = CScaleStyleItem.ScaleSetType.Fixed,
                Value = 100
            });
            cstyle.AddKeyItem(Keys.D0);
            var styleID11 = cstyle.StyleID;
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.Scale150")
            };
            cstyle.AddStyle(new CScaleStyleItem
            {
                SetType = CScaleStyleItem.ScaleSetType.Fixed,
                Value = 150
            });
            var styleID12 = cstyle.StyleID;
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.Border3D")
            };
            cstyle.AddStyle(new CMarginStyleItem
            {
                BorderStyle = 0
            });
            cstyle.AddKeyItem(Keys.D1);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.BorderSolid")
            };
            cstyle.AddStyle(new CMarginStyleItem
            {
                BorderStyle = 1,
                MarginColorRed = byte.MaxValue,
                MarginColorGreen = 128,
                MarginColorBlue = 64,
                MarginSize = 1
            });
            cstyle.AddKeyItem(Keys.D2);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.Windowed")
            };
            cstyle.AddStyle(new CMarginStyleItem
            {
                BorderStyle = 2
            });
            cstyle.AddKeyItem(Keys.D3);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.NoBorder")
            };
            cstyle.AddStyle(new CMarginStyleItem
            {
                BorderStyle = 1,
                MarginSize = 0
            });
            cstyle.AddKeyItem(Keys.L);
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.Close")
            };
            var newCi2 = new CCloseStyleItem();
            cstyle.AddStyle(newCi2);
            cstyle.AddKeyItem(Keys.Q);
            var styleID13 = cstyle.StyleID;
            setunaOption.Styles.Add(cstyle);
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.Basic")
            };
            cstyle.AddStyle(new CMarginStyleItem
            {
                BorderStyle = 0
            });
            cstyle.AddStyle(new COpacityStyleItem
            {
                Absolute = true,
                Opacity = 95
            });
            setunaOption.Styles.Add(cstyle);
            setunaOption.Scrap.CreateStyleID = cstyle.StyleID;
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.Compact")
            };
            cstyle.AddStyle(new CCompactStyleItem
            {
                Opacity = 80,
                SoldLine = false,
                LineColor = Color.Blue.ToArgb()
            });
            setunaOption.Styles.Add(cstyle);
            setunaOption.Scrap.WClickStyleID = cstyle.StyleID;
            cstyle = new CStyle
            {
                StyleID = num++,
                StyleName = Lang.T("DefaultStyle.Trim")
            };
            var newCi3 = new CTrimStyleItem();
            cstyle.AddStyle(newCi3);
            cstyle.AddKeyItem(Keys.T);
            setunaOption.Styles.Add(cstyle);
            setunaOption.Scrap.SubMenuStyles.Add(styleID13);
            setunaOption.Scrap.SubMenuStyles.Add(new CSeparatorStyle().StyleID);
            setunaOption.Scrap.SubMenuStyles.Add(new CScrapListStyle().StyleID);
            setunaOption.Scrap.SubMenuStyles.Add(new CDustBoxStyle().StyleID);
            setunaOption.Scrap.SubMenuStyles.Add(new CDustEraseStyle().StyleID);
            setunaOption.Scrap.SubMenuStyles.Add(new COptionStyle().StyleID);
            setunaOption.Scrap.SubMenuStyles.Add(new CShowVersionStyle().StyleID);
            setunaOption.Scrap.SubMenuStyles.Add(new CSeparatorStyle().StyleID);
            setunaOption.Scrap.SubMenuStyles.Add(styleID);
            setunaOption.Scrap.SubMenuStyles.Add(styleID2);
            setunaOption.Scrap.SubMenuStyles.Add(styleID3);
            setunaOption.Scrap.SubMenuStyles.Add(styleID4);
            setunaOption.Scrap.SubMenuStyles.Add(new CSeparatorStyle().StyleID);
            setunaOption.Scrap.SubMenuStyles.Add(styleID10);
            setunaOption.Scrap.SubMenuStyles.Add(styleID11);
            setunaOption.Scrap.SubMenuStyles.Add(styleID12);
            setunaOption.Scrap.SubMenuStyles.Add(styleID9);
            setunaOption.Scrap.SubMenuStyles.Add(styleID8);
            setunaOption.Scrap.SubMenuStyles.Add(new CSeparatorStyle().StyleID);
            setunaOption.Scrap.SubMenuStyles.Add(styleID5);
            setunaOption.Scrap.SubMenuStyles.Add(styleID6);
            setunaOption.Scrap.SubMenuStyles.Add(styleID7);
            return setunaOption;
        }

        // Token: 0x1700005C RID: 92
        // (get) Token: 0x06000242 RID: 578 RVA: 0x0000CC2C File Offset: 0x0000AE2C
        public static string ConfigFile
        {
            get
            {
                var text = Application.StartupPath;
                text = Path.GetFullPath(Path.Combine(text, ""));
                if (!Directory.Exists(text))
                {
                    Directory.CreateDirectory(text);
                }
                return Path.Combine(text, "SetunaConfig.xml");
            }
        }

        // Token: 0x06000243 RID: 579 RVA: 0x0000CC6C File Offset: 0x0000AE6C
        public SetunaOption()
        {
            Difficult = false;
            hkScrap = new Keys[(int)HotKeyID.__Count__] { Keys.None, Keys.None };
            blHotKey = false;
            Styles = new List<CStyle>();
            Setuna = new SetunaOption.SetunaOptionData();
            Scrap = new SetunaOption.ScrapOptionData();
        }

        // Token: 0x1700005D RID: 93
        // (get) Token: 0x06000245 RID: 581 RVA: 0x0000CCCD File Offset: 0x0000AECD
        // (set) Token: 0x06000244 RID: 580 RVA: 0x0000CCC0 File Offset: 0x0000AEC0
        public string ApplicationPath
        {
            get => Application.ExecutablePath;
            set => Console.WriteLine(ApplicationPath);
        }

        // Token: 0x1700005E RID: 94
        // (get) Token: 0x06000247 RID: 583 RVA: 0x0000CCE1 File Offset: 0x0000AEE1
        // (set) Token: 0x06000246 RID: 582 RVA: 0x0000CCD4 File Offset: 0x0000AED4
        public string ApplicationVersion
        {
            get => Application.ProductVersion;
            set => Console.WriteLine(ApplicationVersion);
        }

        // Token: 0x1700005F RID: 95
        // (get) Token: 0x06000249 RID: 585 RVA: 0x0000CCF5 File Offset: 0x0000AEF5
        // (set) Token: 0x06000248 RID: 584 RVA: 0x0000CCE8 File Offset: 0x0000AEE8
        public string OptionVersion
        {
            get => "1.0";
            set => Console.WriteLine(OptionVersion);
        }

        // Token: 0x0600024A RID: 586 RVA: 0x0000CCFC File Offset: 0x0000AEFC
        public static Type[] GetAllType()
        {
            var styleType = StyleItemDictionary.GetStyleType();
            var arrayList = new ArrayList
            {
                typeof(SetunaOption),
                typeof(CStyle),
                typeof(Color)
            };
            for (var i = 0; i < styleType.Length; i++)
            {
                arrayList.Add(styleType[i]);
            }
            var array = new Type[arrayList.Count];
            for (var j = 0; j < arrayList.Count; j++)
            {
                array[j] = (Type)arrayList[j];
            }
            return array;
        }

        // Token: 0x17000060 RID: 96
        // (get) Token: 0x0600024C RID: 588 RVA: 0x0000CD9C File Offset: 0x0000AF9C
        // (set) Token: 0x0600024B RID: 587 RVA: 0x0000CD93 File Offset: 0x0000AF93
        public Keys[] ScrapHotKeys
        {
            get => ScrapHotKeyDatas;
            set => ScrapHotKeyDatas = value;
        }

        // Token: 0x17000061 RID: 97
        // (get) Token: 0x0600024E RID: 590 RVA: 0x0000CDAD File Offset: 0x0000AFAD
        // (set) Token: 0x0600024D RID: 589 RVA: 0x0000CDA4 File Offset: 0x0000AFA4
        protected Keys[] ScrapHotKeyDatas
        {
            get => hkScrap;
            set => hkScrap = value;
        }

        // Token: 0x17000062 RID: 98
        // (get) Token: 0x06000250 RID: 592 RVA: 0x0000CDBE File Offset: 0x0000AFBE
        // (set) Token: 0x0600024F RID: 591 RVA: 0x0000CDB5 File Offset: 0x0000AFB5
        public bool ScrapHotKeyEnable
        {
            get => blHotKey;
            set => blHotKey = value;
        }

        // Token: 0x06000251 RID: 593 RVA: 0x0000CDC8 File Offset: 0x0000AFC8
        public KeyItemBook GetKeyItemBook()
        {
            var keyItemBook = new KeyItemBook();
            for (var i = 0; i < Styles.Count; i++)
            {
                var cstyle = Styles[i];
                keyItemBook.AddKeyItem(cstyle.KeyItems);
            }
            return keyItemBook;
        }

        // Token: 0x06000252 RID: 594 RVA: 0x0000CE0C File Offset: 0x0000B00C
        public CStyle FindStyle(int styleId)
        {
            CStyle result = null;
            if (styleId == 0)
            {
                return null;
            }
            foreach (var cstyle in Styles)
            {
                if (cstyle.StyleID == styleId)
                {
                    result = cstyle;
                    break;
                }
            }
            return result;
        }

        // Token: 0x06000253 RID: 595 RVA: 0x0000CE70 File Offset: 0x0000B070
        public HotKeyRegistrationResult RegistHotKey(IntPtr handle, HotKeyID keyID)
        {
            // 先反注册，因此对同一句柄重复调用是幂等的。
            UnregistHotKey(handle, keyID);

            var hkScrap = this.hkScrap[(int)keyID];
            if (hkScrap == Keys.None || !blHotKey)
            {
                return HotKeyRegistrationResult.NotEnabled;
            }

            var num = 0;
            if ((hkScrap & Keys.Shift) == Keys.Shift)
            {
                num |= 4;
            }
            if ((hkScrap & Keys.Alt) == Keys.Alt)
            {
                num |= 1;
            }
            if ((hkScrap & Keys.Control) == Keys.Control)
            {
                num |= 2;
            }
            var key = hkScrap & Keys.KeyCode;
            if (SetunaOption.RegisterHotKey(handle, (int)keyID, num, (int)key) == 0)
            {
                return HotKeyRegistrationResult.Failed;
            }

            return HotKeyRegistrationResult.Registered;
        }

        // Token: 0x06000254 RID: 596 RVA: 0x0000CF15 File Offset: 0x0000B115
        public void UnregistHotKey(IntPtr handle, HotKeyID keyID)
        {
            SetunaOption.UnregisterHotKey(handle, (int)keyID);
        }

        // Token: 0x06000255 RID: 597
        [DllImport("user32.dll")]
        private static extern int RegisterHotKey(IntPtr hWnd, int ID, int MOD_KEY, int KEY);

        // Token: 0x06000256 RID: 598
        [DllImport("user32.dll")]
        private static extern int UnregisterHotKey(IntPtr hWnd, int ID);

        // Token: 0x06000257 RID: 599 RVA: 0x0000CF44 File Offset: 0x0000B144
        public object Clone()
        {
            var setunaOption = new SetunaOption
            {
                Difficult = Difficult,
                hkScrap = hkScrap,
                blHotKey = blHotKey,
                Setuna = Setuna,
                Scrap = Scrap,
                MainWindowWidth = MainWindowWidth,
                MainWindowHeight = MainWindowHeight,
                MainWindowDpi = MainWindowDpi
            };
            for (var i = 0; i < Styles.Count; i++)
            {
                setunaOption.Styles.Add(Styles[i].DeepCopy());
            }
            return setunaOption;
        }

        // Token: 0x040000FA RID: 250
        public bool Difficult;

        // Token: 0x040000FB RID: 251
        protected Keys[] hkScrap;

        // Token: 0x040000FC RID: 252
        protected bool blHotKey;

        // Token: 0x040000FE RID: 254
        public SetunaOption.SetunaOptionData Setuna;

        // Token: 0x040000FF RID: 255
        public SetunaOption.ScrapOptionData Scrap;

        // Token: 0x04000100 RID: 256
        public List<CStyle> Styles;

        public int MainWindowWidth;

        public int MainWindowHeight;

        /// <summary>
        /// <see cref="MainWindowWidth"/>／<see cref="MainWindowHeight"/> 是哪一档 DPI 上的
        /// 物理像素。0 表示「未记录」——本变更之前写下的配置就没有这个元素，读回时按当前
        /// 显示器解释一次（见 <see cref="Window.MainWindowGeometry.RestoreWindowSize"/>）。
        /// </summary>
        public int MainWindowDpi;

        // Token: 0x0200003D RID: 61
        public class ScrapOptionData
        {
            // Token: 0x17000063 RID: 99
            // (get) Token: 0x06000259 RID: 601 RVA: 0x0000CFF5 File Offset: 0x0000B1F5
            // (set) Token: 0x06000258 RID: 600 RVA: 0x0000CFD2 File Offset: 0x0000B1D2
            public sbyte InactiveAlphaValue
            {
                get
                {
                    if (_InactiveAlphaValue > 100)
                    {
                        return 100;
                    }
                    if (_InactiveAlphaValue < 1)
                    {
                        return 1;
                    }
                    return _InactiveAlphaValue;
                }
                set
                {
                    if (value > 100)
                    {
                        _InactiveAlphaValue = 100;
                        return;
                    }
                    if (value < 1)
                    {
                        _InactiveAlphaValue = 1;
                        return;
                    }
                    _InactiveAlphaValue = value;
                }
            }

            // Token: 0x17000064 RID: 100
            // (get) Token: 0x0600025B RID: 603 RVA: 0x0000D02C File Offset: 0x0000B22C
            // (set) Token: 0x0600025A RID: 602 RVA: 0x0000D015 File Offset: 0x0000B215
            public sbyte InactiveLineValue
            {
                get
                {
                    if (_InactiveLineValue > 10)
                    {
                        return 10;
                    }
                    return _InactiveLineValue;
                }
                set
                {
                    if (value > 10)
                    {
                        _InactiveLineValue = 10;
                        return;
                    }
                    _InactiveLineValue = value;
                }
            }

            // Token: 0x17000065 RID: 101
            // (get) Token: 0x0600025D RID: 605 RVA: 0x0000D064 File Offset: 0x0000B264
            // (set) Token: 0x0600025C RID: 604 RVA: 0x0000D041 File Offset: 0x0000B241
            public sbyte MouseOverAlphaValue
            {
                get
                {
                    if (_MouseOverAlphaValue > 100)
                    {
                        return 100;
                    }
                    if (_MouseOverAlphaValue < 1)
                    {
                        return 1;
                    }
                    return _MouseOverAlphaValue;
                }
                set
                {
                    if (value > 100)
                    {
                        _MouseOverAlphaValue = 100;
                        return;
                    }
                    if (value < 1)
                    {
                        _MouseOverAlphaValue = 1;
                        return;
                    }
                    _MouseOverAlphaValue = value;
                }
            }

            // Token: 0x17000066 RID: 102
            // (get) Token: 0x0600025F RID: 607 RVA: 0x0000D09B File Offset: 0x0000B29B
            // (set) Token: 0x0600025E RID: 606 RVA: 0x0000D084 File Offset: 0x0000B284
            public sbyte MouseOverLineValue
            {
                get
                {
                    if (_MouseOverLineValue > 10)
                    {
                        return 10;
                    }
                    return _MouseOverLineValue;
                }
                set
                {
                    if (value > 10)
                    {
                        _MouseOverLineValue = 10;
                        return;
                    }
                    _MouseOverLineValue = value;
                }
            }

            // Token: 0x06000260 RID: 608 RVA: 0x0000D0B0 File Offset: 0x0000B2B0
            public ScrapOptionData()
            {
                CreateStyleID = 0;
                WClickStyleID = 0;
                ImageDrag = true;
                InactiveAlphaChange = true;
                InactiveAlphaValue = 90;
                InactiveLineChange = false;
                InactiveLineValue = 1;
                InactiveLineColor = new RGBColor(0, 0, 0);
                MouseOverAlphaChange = true;
                MouseOverAlphaValue = 10;
                MouseOverLineChange = false;
                MouseOverLineValue = 1;
                MouseOverLineColor = new RGBColor(0, 0, 0);
                SubMenuStyles = new List<int>();
            }

            // Token: 0x04000101 RID: 257
            public int CreateStyleID;

            // Token: 0x04000102 RID: 258
            public int WClickStyleID;

            // Token: 0x04000103 RID: 259
            public bool ImageDrag;

            // Token: 0x04000104 RID: 260
            public bool InactiveAlphaChange;

            // Token: 0x04000105 RID: 261
            private sbyte _InactiveAlphaValue;

            // Token: 0x04000106 RID: 262
            public bool InactiveLineChange;

            // Token: 0x04000107 RID: 263
            private sbyte _InactiveLineValue;

            // Token: 0x04000108 RID: 264
            public RGBColor InactiveLineColor;

            // Token: 0x04000109 RID: 265
            public bool MouseOverAlphaChange;

            // Token: 0x0400010A RID: 266
            private sbyte _MouseOverAlphaValue;

            // Token: 0x0400010B RID: 267
            public bool MouseOverLineChange;

            // Token: 0x0400010C RID: 268
            private sbyte _MouseOverLineValue;

            // Token: 0x0400010D RID: 269
            public RGBColor MouseOverLineColor;

            // Token: 0x0400010E RID: 270
            public List<int> SubMenuStyles;
        }

        // Token: 0x0200004F RID: 79
        public class SetunaOptionData
        {
            // Token: 0x17000070 RID: 112
            // (get) Token: 0x060002CC RID: 716 RVA: 0x0000F634 File Offset: 0x0000D834
            public bool ClickCapture => ClickCapture7 | ClickCapture8 | ClickCapture9 | ClickCapture4 | ClickCapture6 | ClickCapture1 | ClickCapture2 | ClickCapture3;

            // Token: 0x17000071 RID: 113
            // (get) Token: 0x060002CD RID: 717 RVA: 0x0000F670 File Offset: 0x0000D870
            public bool[] ClickCaptureValue => new bool[]
                    {
                        false,
                        ClickCapture1,
                        ClickCapture2,
                        ClickCapture3,
                        ClickCapture4,
                        false,
                        ClickCapture6,
                        ClickCapture7,
                        ClickCapture8,
                        ClickCapture9
                    };

            // Token: 0x060002CE RID: 718 RVA: 0x0000F6D8 File Offset: 0x0000D8D8
            public SetunaOptionData()
            {
                AppType = SetunaOption.SetunaOptionData.ApplicationType.ApplicationMode;
                ShowMainWindow = true;
                ShowSplashWindow = true;
                SelectLineSolid = true;
                SelectLineColorR = Color.Blue.R;
                SelectLineColorG = Color.Blue.G;
                SelectLineColorB = Color.Blue.B;
                SelectBackColorR = Color.DarkBlue.R;
                SelectBackColorG = Color.DarkBlue.G;
                SelectBackColorB = Color.DarkBlue.B;
                SelectAreaTransparent = 80;
                DustBoxEnable = true;
                DustBoxCapacity = 5;
                ClickCapture7 = false;
                ClickCapture8 = false;
                ClickCapture9 = false;
                ClickCapture4 = false;
                ClickCapture6 = false;
                ClickCapture1 = false;
                ClickCapture2 = false;
                ClickCapture3 = false;

                TopMostEnabled = false;
                CursorEnabled = false;
                FullscreenCursor = false;
                MagnifierEnabled = false;

                FullscreenCursorSolid = true;
                FullscreenCursorColorR = Color.Orange.R;
                FullscreenCursorColorG = Color.Orange.G;
                FullscreenCursorColorB = Color.Orange.B;

                BackgroundTransparentEnabled = false;
            }

            // Token: 0x17000072 RID: 114
            // (get) Token: 0x060002CF RID: 719 RVA: 0x0000F7C9 File Offset: 0x0000D9C9
            public Color SelectLineColor => Color.FromArgb(SelectLineColorR, SelectLineColorG, SelectLineColorB);

            // Token: 0x17000073 RID: 115
            // (get) Token: 0x060002D0 RID: 720 RVA: 0x0000F7E2 File Offset: 0x0000D9E2
            public Color SelectBackColor => Color.FromArgb(SelectBackColorR, SelectBackColorG, SelectBackColorB);

            // Token: 0x04000140 RID: 320
            public SetunaOption.SetunaOptionData.ApplicationType AppType;

            // Token: 0x04000141 RID: 321
            public bool ShowMainWindow;

            // Token: 0x04000142 RID: 322
            public SetunaOption.SetunaOptionData.OpeningType DupType;

            // Token: 0x04000143 RID: 323
            public bool ShowSplashWindow;

            // Token: 0x04000144 RID: 324
            public bool SelectLineSolid;

            // Token: 0x04000145 RID: 325
            public byte SelectLineColorR;

            // Token: 0x04000146 RID: 326
            public byte SelectLineColorG;

            // Token: 0x04000147 RID: 327
            public byte SelectLineColorB;

            // Token: 0x04000148 RID: 328
            public byte SelectBackColorR;

            // Token: 0x04000149 RID: 329
            public byte SelectBackColorG;

            // Token: 0x0400014A RID: 330
            public byte SelectBackColorB;

            // Token: 0x0400014B RID: 331
            public int SelectAreaTransparent;

            // Token: 0x0400014C RID: 332
            public bool DustBoxEnable;

            // Token: 0x0400014D RID: 333
            public ushort DustBoxCapacity;

            // Token: 0x0400014E RID: 334
            public bool ClickCapture7;

            // Token: 0x0400014F RID: 335
            public bool ClickCapture8;

            // Token: 0x04000150 RID: 336
            public bool ClickCapture9;

            // Token: 0x04000151 RID: 337
            public bool ClickCapture4;

            // Token: 0x04000152 RID: 338
            public bool ClickCapture6;

            // Token: 0x04000153 RID: 339
            public bool ClickCapture1;

            // Token: 0x04000154 RID: 340
            public bool ClickCapture2;

            // Token: 0x04000155 RID: 341
            public bool ClickCapture3;

            public bool TopMostEnabled;

            public bool CursorEnabled;

            public bool FullscreenCursor;

            public bool MagnifierEnabled;

            public bool BackgroundTransparentEnabled;

            public Color FullscreenCursorLineColor => Color.FromArgb(FullscreenCursorColorR, FullscreenCursorColorG, FullscreenCursorColorB);
            public bool FullscreenCursorSolid;
            public byte FullscreenCursorColorR;
            public byte FullscreenCursorColorG;
            public byte FullscreenCursorColorB;

            /// <summary>
            /// 界面语言。空串或元素缺失表示「跟随系统区域设置」。
            /// <para>
            /// 用字符串而不是枚举：<see cref="System.Xml.Serialization.XmlSerializer"/>
            /// 遇到无法识别的枚举值会抛异常，那会让「新版本写入了未来语言的配置被旧版本
            /// 读取」变成启动失败。字符串则退化成一个未知值，可以安全地按「跟随系统」处理。
            /// </para>
            /// <para>
            /// 新增一个 XML 元素本身是向后兼容的：旧配置文件缺这个元素时反序列化得到
            /// <c>null</c>，与空串同义，因此不需要迁移代码。
            /// </para>
            /// </summary>
            public string Language;



            // Token: 0x02000052 RID: 82
            public enum ApplicationType
            {
                // Token: 0x040001C4 RID: 452
                ApplicationMode = 1,
                // Token: 0x040001C5 RID: 453
                ResidentMode = 16
            }

            // Token: 0x02000056 RID: 86
            public enum OpeningType
            {
                // Token: 0x040001CA RID: 458
                Normal,
                // Token: 0x040001CB RID: 459
                Capture
            }
        }
    }
}
