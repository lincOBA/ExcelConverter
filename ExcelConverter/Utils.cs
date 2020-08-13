﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace ExcelConverter
{
    public class Utils
    {
        private const string favFileName = "fav_gbk.json";
        private const string tmpFavFileName = "fav.json"; //兼容
        private const string treeFileName = "tree.json";
        private const string tmpTreeFileName = "tree_tmp.json";
        private const string combineFileName = "conv_zl_force_conv_combine.json";
        private static string[] _batFileStrLine;
        private static Dictionary<int, string[]> _batFileStrSplitDict = new Dictionary<int, string[]>();
        private static Queue<Action> _cmdQueue = new Queue<Action>();

        public static string WorkingPath = "";
        public static void InitWorkingPath()
        {
            //string runningPath = AppDomain.CurrentDomain.BaseDirectory;
            //var info = Directory.GetParent(System.Reflection.Assembly.GetExecutingAssembly().Location);
            //string path = info.FullName;

            string path = "";
#if DEBUG
            path = "C:\\Work\\data";
#else
            path = Environment.CurrentDirectory;
#endif
            Utils.WorkingPath = path;
        }

        public static void GenFileTree()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(DoInBackgroundThread);
        }

        private static void DoInBackgroundThread(object state)
        {
            var xlsPath = Utils.WorkingPath + "\\xls\\";
            if (!Directory.Exists(xlsPath))
            {
                EventDispatcher.SendEvent(TaskType.SearchError, "xls文件夹不存在");
                return;
            }

            TreeNode rootNode = new TreeNode();
            Search(rootNode, xlsPath, NodeType.Dir);
            SaveFileTree(rootNode);
            EventDispatcher.SendEvent(TaskType.FinishedSearch, rootNode);
            GC.Collect();
        }

        private static void Search(TreeNode treeNode, string path, NodeType nodeType)
        {
            //root.Path = path;
            treeNode.Path = GetRelativePath(path);
            treeNode.Name = GetFileName(path);
            treeNode.SubSheetName = GetSheetListName(path, nodeType);

            if (nodeType == NodeType.File)
            {
                treeNode.Type = NodeType.File;
                return;
            }

            treeNode.IsExpanded = false;
            treeNode.Type = NodeType.Dir;
            var files = Directory.GetFiles(path, "*.xlsx", SearchOption.TopDirectoryOnly);
            //root.ChildFileName = new List<string>(files);
            var childDirPath = Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly);
            List<TreeNode> childNodes = new List<TreeNode>();
            for (int i = 0; i < childDirPath.Length; i++)
            {
                TreeNode node = new TreeNode();
                Search(node, childDirPath[i], NodeType.Dir);
                childNodes.Add(node);

                EventDispatcher.SendEvent(TaskType.UpdateSearchProgress, (i + 1f) / childDirPath.Length);
            }

            for (int i = 0; i < files.Length; i++)
            {
                TreeNode node = new TreeNode();
                if (files[i].Contains("~$"))
                    continue;

                Search(node, files[i], NodeType.File);
                childNodes.Add(node);
            }

            treeNode.Child = childNodes;
        }

        public static void FilterTree(TreeNode node, string filterStr, ref TreeNode filterNode)
        {
            filterNode = CloneTree(node);
            FilterTree(ref filterNode, filterStr);
            GC.Collect();
        }

        private static bool FilterTree(ref TreeNode node, string filterStr)
        {
            node.IsExpanded = true;
            var childs = node.Child;
            if (childs == null)
                return false;

            //当前文件夹名字匹配上就不用找子文件(夹)了，直接返回
            if (node.Name.Contains(filterStr, StringComparison.OrdinalIgnoreCase))
            {
                node.IsMatch = true;
                FindMatchFolderMatchFile(node, filterStr);
                return true;
            }

            bool bFind = false;
            for (int i = 0; i < childs.Count; i++)
            {
                TreeNode child = childs[i];
                if (child.IsFile)
                {
                    if (!child.MatchSearch(filterStr))
                    {
                        childs.RemoveAt(i);
                        i--;
                    }
                    else
                    {
                        child.IsMatch = true;
                        bFind = true;
                    }
                }
                else
                {
                    bool res = FilterTree(ref child, filterStr);
                    if (!res)
                    {
                        childs.RemoveAt(i);
                        i--;
                    }
                    else
                    {
                        bFind = true;
                    }
                }
            }
            return bFind;
        }

        //
        private static void FindMatchFolderMatchFile(TreeNode matchNode, string filterStr)
        {
            if (!matchNode.IsFile && matchNode.Child != null)
            {
                for (int i = 0; i < matchNode.Child.Count; i++)
                {
                    TreeNode childNode = matchNode.Child[i];
                    if (childNode.MatchSearch(filterStr))
                    {
                        childNode.IsMatch = true;
                    }
                    FindMatchFolderMatchFile(childNode, filterStr);
                }
            }
        }

        private static TreeNode CloneTree(TreeNode tree)
        {
            TreeNode cloneNode = tree.Clone();
            if (tree.Child != null)
            {
                for (int i = 0; i < tree.Child.Count; i++)
                {
                    cloneNode.Child[i] = CloneTree(tree.Child[i]);
                }
            }
            return cloneNode;
        }

        private static void SaveFileTree(TreeNode treeNode)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var jsonStr = JsonSerializer.Serialize(treeNode, options);
            FileStream fileStream = File.Create(WorkingPath + "\\" + treeFileName);
            fileStream.Write(Encoding.GetEncoding("GBK").GetBytes(jsonStr));
            fileStream.Flush(true);
            fileStream.Close();
        }

        public static void SaveFav(List<TreeNode> pathList)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var jsonStr = JsonSerializer.Serialize(pathList, options);
            string saveFavDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            FileStream fileStream = File.Create(saveFavDir + "\\" + favFileName);
            fileStream.Write(Encoding.GetEncoding("GBK").GetBytes(jsonStr));
            fileStream.Flush(true);
            fileStream.Close();
        }

        public static TreeNode ReadTree()
        {
            string filePath = WorkingPath + "\\" + treeFileName;
            TreeNode root = null;
            try
            {
                if (File.Exists(filePath))
                {
                    var bytes = File.ReadAllBytes(filePath);
                    var str = Encoding.GetEncoding("GBK").GetString(bytes);
                    root = JsonSerializer.Deserialize<TreeNode>(str);
                }
                else
                {
                    var bytes = File.ReadAllBytes(WorkingPath + "\\" + tmpTreeFileName);
                    var str = Encoding.GetEncoding("GBK").GetString(bytes);
                    root = JsonSerializer.Deserialize<TreeNode>(str);
                }
            }
            catch (Exception)
            {
                if (root == null)
                    root = new TreeNode();
            }
            return root;
        }
        
        public static List<TreeNode> ReadFav()
        {
            string saveFavDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string filePath = saveFavDir + "\\" + favFileName;
            List<TreeNode> list = null;
            try
            {
                if (File.Exists(filePath))
                {
                    var bytes = File.ReadAllBytes(filePath);
                    var str = Encoding.GetEncoding("GBK").GetString(bytes);
                    list = JsonSerializer.Deserialize<List<TreeNode>>(str);
                }
                else
                {
                    var bytes = File.ReadAllBytes(saveFavDir + "\\" + tmpFavFileName);
                    var str = Encoding.UTF8.GetString(bytes);
                    list = JsonSerializer.Deserialize<List<TreeNode>>(str);
                }
            }
            catch (Exception)
            {
                if (list == null)
                    list = new List<TreeNode>();
            }
            return list;
        }

        public static void ConvertExcel(List<TreeNode> convertList)
        {
            List<string> pathList = new List<string>();
            GetBatCmd();

            ConvertToPath(convertList, pathList);
            CopyXlsToTmpDir(pathList);
            PushCommand(CovertCsv);
            PushCommand(ConvertBin);
            PushCommand(GC.Collect);
        }

        public static void CleanConvert()
        {
            _cmdQueue.Clear();
            if (_curProcess != null && !_curProcess.HasExited)
            {
                _curProcess.Kill();
            }
            _curProcess = null;
        }

        private static void CopyXlsToTmpDir(List<string> pathList)
        {
            string copyStr = GetEnterDirStr() + @"
rd /S /Q .\xls_tmp
rd /S /Q .\csv
md .\xls_tmp
md .\csv
";

            List<string[]> combineList = null;
            string combineFile = $"{WorkingPath}\\{combineFileName}";
            if (File.Exists(combineFile))
            {
                var bytes = File.ReadAllBytes(combineFile);
                var str = Encoding.GetEncoding("GBK").GetString(bytes);
                combineList = JsonSerializer.Deserialize<List<string[]>>(str);
            }

            for (int i = 0; i < pathList.Count; i++)
            {
                copyStr += "copy /y " + pathList[i] + " " + WorkingPath + "\\xls_tmp\\" + GetFileName(pathList[i]) + "\r\n";
                //var fileName = GetFileName(pathList[i]);
                //pathList[i] = pathList[i].Remove(pathList[i].LastIndexOf("\\xls\\")) + "\\xls_tmp\\" + fileName;

                //合表
                if (combineList != null)
                {
                    string[] copyArr = null;
                    int inIndex2 = -1;
                    for (int j = 0; j < combineList.Count; j++)
                    {
                        var arr = combineList[j];
                        for (int k = 0; k < arr.Length; k++)
                        {
                            if (pathList[i].EndsWith(arr[k]))
                            {
                                copyArr = arr;
                                inIndex2 = k;
                                break;
                            }
                        }
                    }

                    if (copyArr == null) continue;

                    //copy extra excel
                    for (int copyIdx = 0; copyIdx < copyArr.Length; copyIdx++)
                    {
                        if (copyIdx != inIndex2)
                        {
                            string extraPathRelativeWorkPath = copyArr[copyIdx];
                            copyStr += $"copy /y {WorkingPath}\\xls\\{extraPathRelativeWorkPath} {WorkingPath}\\xls_tmp\\{GetFileName(extraPathRelativeWorkPath)}\r\n";
                        }
                    }
                }
            }

            ExecuteBatCommand(copyStr, true);
        }

        private static void CovertCsv()
        {
            string middle = WorkingPath + "\\x2c\\xls2csv " + (WorkingPath + "\\xls_tmp\\ ") + (WorkingPath + "\\csv " + WorkingPath + "\\x2c.x2c\r\n\r\n");
            ExecuteBatCommand(middle);
        }

        private static void ConvertBin()
        {
            var prefix = @"set path=C:\Windows\System32;%path%" +
                     GetEnterDirStr();
            var csvList = Directory.GetFiles($"{WorkingPath}\\csv", "*.csv");
            string commandLines = prefix + @"rd /S /Q .\bin
rd /S /Q .\bin_cli
md .\bin
call path_define.bat
md .\bin_cli

del  build_err.log
del  build_info.log

call SshGenXml.exe

";
            var lineIndexList = new List<int>();
            for (int fileIdx = 0; fileIdx < csvList.Length; fileIdx++)
            {
                for (int lineNum = 0; lineNum < _batFileStrSplitDict.Count; lineNum++)
                {
                    string csvFileName = csvList[fileIdx];
                    string withoutExtension = Path.GetFileNameWithoutExtension(csvFileName);
                    var arr = _batFileStrSplitDict[lineNum];
                    if (arr.Length > 3)
                    {
                        for (int j = 3; j < arr.Length; j++)
                        {
                            if (arr[j].Equals(withoutExtension, StringComparison.OrdinalIgnoreCase) &&
                                !lineIndexList.Contains(lineNum))
                            {
                                lineIndexList.Add(lineNum);
                            }
                        }
                    }
                }
            }

            if (lineIndexList.Count <= 0)
                return;

            lineIndexList.Sort();
            for (var i = 0; i < lineIndexList.Count; i++)
            {
                int lineNum = lineIndexList[i];
                var lineCmd = _batFileStrLine[lineNum];
                commandLines += lineCmd + "\r\n";
            }

            ExecuteBatCommand(commandLines + "\r\n@echo 转表结束");
        }

        /// <summary>
        /// 为了实现非堵塞的执行，弄个队列来执行。
        /// </summary>
        /// <param name="action">操作的Action</param>
        private static void PushCommand(Action action)
        {
            if (_cmdQueue.Count == 0)
            {
                action();
            }
            _cmdQueue.Enqueue(action);
        }

        private static Process _curProcess;
        private static void ExecuteBatCommand(string command, bool wait = false)
        {
            string batPath = CreateTmpBat(command);
            var process = Process.Start(new ProcessStartInfo(batPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.GetEncoding("GBK"),
            });

            if (process == null) return;

            _curProcess = process;
            process.OutputDataReceived += (sender, args) =>
            {
                EventDispatcher.SendEvent(TaskType.ConvertOutput, args.Data);
            };
            process.EnableRaisingEvents = true;                      // 启用Exited事件  
            process.Exited += ProcessOnExited;
            process.BeginOutputReadLine();

            if (wait)
                process.WaitForExit();
        }

        private static void ProcessOnExited(object sender, EventArgs e)
        {
            if (sender == _curProcess)
            {
                _curProcess = null;
            }

            NextCommand();
        }

        private static void NextCommand()
        {
            if (_cmdQueue.Count > 0)
                _cmdQueue.Dequeue();
            if (_cmdQueue.Count > 0)
            {
                var cmdAction = _cmdQueue.Dequeue();
                cmdAction();
            }
        }

        private static string CreateTmpBat(string content)
        {
            string batPath = WorkingPath + "\\tmp.bat";
            var file = File.Create(batPath);
            file.Write(Encoding.GetEncoding("GBK").GetBytes(content));
            file.Flush(true);
            file.Close();
            return batPath;
        }

        private static DateTime _lastTime; 
        public static void GetBatCmd()
        {
            string batPath = WorkingPath + "\\策划转表_公共.bat";
            FileInfo file = new FileInfo(batPath);
            if (file.LastWriteTime != _lastTime)
            {
                _batFileStrLine = File.ReadAllLines(batPath, Encoding.GetEncoding("GBK"));
                for (int i = 0; i < _batFileStrLine.Length; i++)
                {
                    _batFileStrSplitDict.Add(i, _batFileStrLine[i].Split(' '));
                }
                _lastTime = file.LastWriteTime;
            }
        }

        public static void ParseBinList(List<BinListNode> saveList)
        {
            string batPath = WorkingPath + "\\策划转表_公共.bat";
            
            var lines = File.ReadAllLines(batPath, Encoding.GetEncoding("GBK"));
            
            List<string> binList = new List<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                if(string.IsNullOrEmpty(lines[i]))
                    continue;

                var arr = lines[i].Split(' ');
                if(arr.Length < 4)
                    continue;

                binList.Clear();
                binList.Add(arr[2]);
                int startIndex = 3;
                if (arr[1] == "do_conv_svr_spec.bat")
                {
                    binList.Add(arr[3]);
                    startIndex = 4;
                }

                for (int binIdx = 0; binIdx < binList.Count; binIdx++)
                {
                    var binName = binList[binIdx];
                    for (int j = startIndex; j < arr.Length; j++)
                    {
                        string sheetName = arr[j];
                        if(string.IsNullOrEmpty(sheetName))
                            continue;

                        BinListNode node = new BinListNode();
                        node.BinName = binName;
                        node.SheetName = sheetName;
                        node.FullName = $"{sheetName} ({binName})";
                        saveList.Add(node);
                    }
                }
            }
        }

        private static void ConvertToPath(List<TreeNode> nodes, List<string> pathList)
        {
            if (nodes.Count <= 0) return;

            for (var i = 0; i < nodes.Count; i++)
            {
                TreeNode node = nodes[i];
                if (node.Type == NodeType.Dir)
                {
                    ConvertToPath(node.Child, pathList);
                }
                else
                {
                    if (!node.Path.Contains("~$"))
                    {
                        pathList.Add(node.GetAbsolutePath());
                    }
                }
            }
        }

        private static string GetEnterDirStr()
        {
            var pathArr = WorkingPath.Split(":");
            var disk = pathArr[0];
            var folderPath = pathArr[1];
            return "\r\n" + disk + ":\r\n"
                + "cd " + folderPath + "\r\n";
        }

        private static List<string> GetSheetListName(string fullPath, NodeType nodeType)
        {
            if (nodeType == NodeType.File)
            {
                List<string> sheetNames = new List<string>();
                GetSubSheetNames(fullPath, sheetNames);
                return sheetNames;
            }

            return null;
        }

        public static string GetFileName(string fullPath)
        {
            var lastIndexOf = fullPath.LastIndexOf("\\", StringComparison.Ordinal) + 1;
            var name = fullPath.Substring(lastIndexOf, fullPath.Length - lastIndexOf);
            return name;
        }

        public static void GetSubSheetNames(string xlsPath, List<string> sheetNames)
        {
            FileInfo inputStream = new FileInfo(xlsPath);

            var newXlsPath = xlsPath;
            if (IsFileInUse(xlsPath))
            {
                string destFileName = $"{WorkingPath}/xls_tmp/{inputStream.Name}";
                if(File.Exists(destFileName))
                    File.Delete(destFileName);
                File.Copy(xlsPath, destFileName);
                newXlsPath = destFileName;
                inputStream = new FileInfo(newXlsPath);
            }

            if(File.Exists(newXlsPath))
            {
                IWorkbook workbook = new XSSFWorkbook(inputStream);
                int sheetCnt = workbook.NumberOfSheets;
                for (int j = 0; j < sheetCnt; j++)
                {
                    ISheet sheet = workbook.GetSheetAt(j);
                    var sheetName = sheet.SheetName;
                    sheetNames.Add(sheetName);
                }
            }
        }

        public static bool IsFileInUse(string fileName)
        {
            bool inUse = true;

            FileStream fs = null;
            try
            {
                fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.None);
                inUse = false;
            }
            catch
            {
            }
            finally
            {
                if (fs != null)
                    fs.Close();
            }
            return inUse;//true表示正在使用,false没有使用
        }

        public static int SortList(TreeNode node1, TreeNode node2)
        {
            if(node1.Type != node2.Type)
            {
                return (node1.Type - node2.Type);
            }
            else
            {
                return node1.SingleFileName.CompareTo(node2.SingleFileName);
            }
        }

        public static string GetRelativePath(string fullPath)
        {
            return fullPath.Replace(WorkingPath, "");
        }

        public static string GetAbsolutePath(string relativePath)
        {
            return $"{WorkingPath}{relativePath}";
        }
    }
}
