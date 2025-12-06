using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using Wit.SDK.Modular.Sensor.Modular.DataProcessor.Constant;
using Wit.SDK.Modular.WitSensorApi.Modular.BWT901BLE;
using Wit.SDK.Device.Device.Device.DKey;
using Wit.Bluetooth.WinBlue.Utils;
using Wit.Bluetooth.WinBlue.Interface;

namespace Wit.Example_BWT901BLE
{
    public partial class Form1 : Form
    {
        /// <summary>
        /// 蓝牙管理器
        /// Bluetooth manager
        /// </summary>
        private IWinBlueManager WitBluetoothManager = WinBlueFactory.GetInstance();

        /// <summary>
        /// 找到的设备
        /// Found device
        /// </summary>
        private Dictionary<string, Bwt901ble> FoundDeviceDict = new Dictionary<string, Bwt901ble>();

        /// <summary>
        /// 控制自动刷新数据线程是否工作
        /// Control whether the automatic refresh data thread works
        /// </summary>
        public bool EnableRefreshDataTh { get; private set; }

        // 踏频计算相关变量
        private double lastAngleZ = 0;
        private long lastTime = 0;
        private double cadence = 0; // 踏频（转/分钟）
        private int rotationCount = 0;
        private long lastRotationTime = 0;
        private List<long> rotationTimes = new List<long>();
        private double totalAngleChange = 0; // 累积角度变化
        private double lastRecordedAngle = 0; // 上次记录的角度

        // 踏频显示控件
        private Label cadenceLabel;
        private Label rotationCountLabel;
        private Label currentAngleLabel;
        private Label accumulatedAngleLabel;
        private Label statusLabel;
        private Button resetButton;

        /// <summary>
        /// 构造
        /// Structure
        /// </summary>
        public Form1()
        {
            InitializeComponent();
            InitializeCadenceUI();
        }

        /// <summary>
        /// 初始化踏频计算UI
        /// Initialize cadence calculation UI
        /// </summary>
        private void InitializeCadenceUI()
        {
            // 创建踏频显示区域
            GroupBox cadenceGroupBox = new GroupBox();
            cadenceGroupBox.Text = "自行车踏频计算";
            cadenceGroupBox.Size = new Size(300, 200);
            cadenceGroupBox.Location = new Point(500, 20);
            this.Controls.Add(cadenceGroupBox);

            // 踏频显示
            cadenceLabel = new Label();
            cadenceLabel.Text = "踏频: 0 RPM";
            cadenceLabel.Font = new Font("Microsoft Sans Serif", 12, FontStyle.Bold);
            cadenceLabel.ForeColor = Color.Blue;
            cadenceLabel.Size = new Size(200, 25);
            cadenceLabel.Location = new Point(20, 25);
            cadenceGroupBox.Controls.Add(cadenceLabel);

            // 旋转计数显示
            rotationCountLabel = new Label();
            rotationCountLabel.Text = "旋转计数: 0";
            rotationCountLabel.Font = new Font("Microsoft Sans Serif", 10);
            rotationCountLabel.ForeColor = Color.DarkGray;
            rotationCountLabel.Size = new Size(200, 20);
            rotationCountLabel.Location = new Point(20, 55);
            cadenceGroupBox.Controls.Add(rotationCountLabel);

            // 当前角度显示
            currentAngleLabel = new Label();
            currentAngleLabel.Text = "当前角度: 0°";
            currentAngleLabel.Font = new Font("Microsoft Sans Serif", 9);
            currentAngleLabel.ForeColor = Color.DarkGray;
            currentAngleLabel.Size = new Size(200, 20);
            currentAngleLabel.Location = new Point(20, 80);
            cadenceGroupBox.Controls.Add(currentAngleLabel);

            // 累积角度显示
            accumulatedAngleLabel = new Label();
            accumulatedAngleLabel.Text = "累积角度: 0°";
            accumulatedAngleLabel.Font = new Font("Microsoft Sans Serif", 9);
            accumulatedAngleLabel.ForeColor = Color.DarkGray;
            accumulatedAngleLabel.Size = new Size(200, 20);
            accumulatedAngleLabel.Location = new Point(20, 105);
            cadenceGroupBox.Controls.Add(accumulatedAngleLabel);

            // 状态显示
            statusLabel = new Label();
            statusLabel.Text = "状态: 等待检测旋转...";
            statusLabel.Font = new Font("Microsoft Sans Serif", 9);
            statusLabel.ForeColor = Color.Green;
            statusLabel.Size = new Size(200, 20);
            statusLabel.Location = new Point(20, 130);
            cadenceGroupBox.Controls.Add(statusLabel);

            // 重置按钮
            resetButton = new Button();
            resetButton.Text = "重置计数";
            resetButton.Size = new Size(80, 25);
            resetButton.Location = new Point(20, 155);
            resetButton.Click += ResetCadenceCalculation;
            cadenceGroupBox.Controls.Add(resetButton);
        }

        /// <summary>
        /// 窗体加载时
        /// When the form is loaded
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Form1_Load(object sender, EventArgs e)
        {
            // 开启数据刷新线程
            // Enable data refresh thread
            Thread thread = new Thread(RefreshDataTh);
            thread.IsBackground = true;
            EnableRefreshDataTh = true;
            thread.Start();

            // 开启踏频计算更新线程
            Thread cadenceThread = new Thread(UpdateCadenceUI);
            cadenceThread.IsBackground = true;
            cadenceThread.Start();
        }

        /// <summary>
        /// 窗体关闭时
        /// When the form is closed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // 关闭刷新数据线程
            // Close refresh data thread
            EnableRefreshDataTh = false;
            // 关闭蓝牙搜索
            // Turn off Bluetooth search
            stopScanButton_Click(null, null);
            Process.GetCurrentProcess().Kill();
        }

        /// <summary>
        /// 开始搜索
        /// Starting the Search
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void startScanButton_Click(object sender, EventArgs e)
        {
            // 清除找到的设备
            // Clear found devices
            FoundDeviceDict.Clear();

            // 关闭之前打开的设备
            // Close previously opened devices
            foreach (var device in FoundDeviceDict.Values)
            {
                device.Close();
            }

            WitBluetoothManager.OnDeviceFound += this.WitBluetoothManager_OnDeviceFound;
            WitBluetoothManager.StartScan();
        }

        /// <summary>
        /// 当搜索到蓝牙设备时会回调这个方法
        /// Call back this method when Bluetooth devices are found
        /// </summary>
        /// <param name="mac"></param>
        /// <param name="deviceName"></param>
        private void WitBluetoothManager_OnDeviceFound(string mac, string deviceName)
        {
            // 名称过滤
            // Name filtering
            if (deviceName != null && deviceName.Contains("WT"))
            {
                if (!FoundDeviceDict.ContainsKey(mac))
                {
                    Bwt901ble bWT901BLE = new Bwt901ble(mac, deviceName);
                    FoundDeviceDict.Add(mac, bWT901BLE);
                    // 打开这个设备
                    // Open this device
                    bWT901BLE.Open();
                    bWT901BLE.OnRecord += BWT901BLE_OnRecord;
                }
            }
        }

        /// <summary>
        /// 当传感器数据刷新时会调用这里，您可以在这里记录数据
        /// This will be called when the sensor data is refreshed, where you can record the data
        /// </summary>
        /// <param name="BWT901BLE"></param>
        private void BWT901BLE_OnRecord(Bwt901ble BWT901BLE)
        {
            string text = GetDeviceData(BWT901BLE);
            Debug.WriteLine(text);

            // 更新踏频计算
            UpdateCadenceCalculation(BWT901BLE);
        }

        /// <summary>
        /// 停止搜索
        /// stop searching
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void stopScanButton_Click(object sender, EventArgs e)
        {
            // 让蓝牙管理器停止搜索
            // Stop Bluetooth Manager Search
            WitBluetoothManager.StopScan();
        }

        /// <summary>
        /// 设备状态发生时会调这个方法
        /// This method will be called when the device status occurs
        /// </summary>
        /// <param name="macAddr"></param>
        /// <param name="mType"></param>
        /// <param name="sMsg"></param>
        private void OnDeviceStatu(string macAddr, int mType, string sMsg)
        {
            if (mType == 20)
            {
                // 断开连接
                // Disconnect
                Debug.WriteLine(macAddr + "Disconnect");
            }

            if (mType == 11)
            {
                // 连接失败
                // Connect failed
                Debug.WriteLine(macAddr + "Connect failed");
            }

            if (mType == 10)
            {
                // 连接成功
                // Successfully connected
                Debug.WriteLine(macAddr + "Successfully connected");
            }
        }

        /// <summary>
        /// 刷新数据线程
        /// Refresh Data Thread
        /// </summary>
        private void RefreshDataTh()
        {
            while (EnableRefreshDataTh)
            {
                // 多设备的展示数据
                // Display data for multiple devices
                string DeviceData = "";
                Thread.Sleep(100);
                // 刷新所有连接设备的数据
                // Refresh data for all connected devices
                foreach (var bWT901BLE in FoundDeviceDict.Values)
                {
                    if (bWT901BLE.IsOpen())
                    {
                        DeviceData += GetDeviceData(bWT901BLE) + "\r\n";
                        Debug.WriteLine("22222 " + DateTime.Now);    // 会输出到 Output
                    }
                }

                // 添加踏频数据到显示
                DeviceData += GetCadenceData() + "\r\n";

                if (dataRichTextBox.InvokeRequired)
                {
                    dataRichTextBox.Invoke(new Action(() =>
                    {
                        dataRichTextBox.Text = DeviceData;
                    }));
                }
                else
                {
                    dataRichTextBox.Text = DeviceData;
                }
            }
        }

        /// <summary>
        /// 获得设备的数据
        /// Obtaining device data
        /// </summary>
        private string GetDeviceData(Bwt901ble BWT901BLE)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(BWT901BLE.GetDeviceName()).Append("\n");
            // 加速度
            // Acc
            builder.Append("AccX").Append(":").Append(BWT901BLE.GetDeviceData(WitSensorKey.AccX)).Append("g \t");
            builder.Append("AccY").Append(":").Append(BWT901BLE.GetDeviceData(WitSensorKey.AccY)).Append("g \t");
            builder.Append("AccZ").Append(":").Append(BWT901BLE.GetDeviceData(WitSensorKey.AccZ)).Append("g \n");
            // 角速度
            // Gyro
            builder.Append("GyroX").Append(":").Append(BWT901BLE.GetDeviceData(WitSensorKey.AsX)).Append("°/s \t");
            builder.Append("GyroY").Append(":").Append(BWT901BLE.GetDeviceData(WitSensorKey.AsY)).Append("°/s \t");
            builder.Append("GyroZ").Append(":").Append(BWT901BLE.GetDeviceData(WitSensorKey.AsZ)).Append("°/s \n");
            // 角度
            // Angle
            builder.Append("AngleX").Append(":").Append(BWT901BLE.GetDeviceData(WitSensorKey.AngleX)).Append("° \t");
            builder.Append("AngleY").Append(":").Append(BWT901BLE.GetDeviceData(WitSensorKey.AngleY)).Append("° \t");
            builder.Append("AngleZ").Append(":").Append(BWT901BLE.GetDeviceData(WitSensorKey.AngleZ)).Append("° \n");
            // 磁场
            // Mag
            builder.Append("MagX").Append(":").Append(BWT901BLE.GetDeviceData(WitSensorKey.HX)).Append("uT \t");
            builder.Append("MagY").Append(":").Append(BWT901BLE.GetDeviceData(WitSensorKey.HY)).Append("uT \t");
            builder.Append("MagZ").Append(":").Append(BWT901BLE.GetDeviceData(WitSensorKey.HZ)).Append("uT \n");
            // 版本号
            // VersionNumber
            builder.Append("VersionNumber").Append(":").Append(BWT901BLE.GetDeviceData(WitSensorKey.VersionNumber)).Append("\n");
            return builder.ToString();
        }

        /// <summary>
        /// 获得踏频数据
        /// Obtaining cadence data
        /// </summary>
        private string GetCadenceData()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("=== 踏频计算数据 ===\n");
            builder.Append("踏频").Append(":").Append(cadence.ToString("F1")).Append(" RPM \t");
            builder.Append("旋转计数").Append(":").Append(rotationCount).Append(" 圈\n");
            builder.Append("累积角度").Append(":").Append(totalAngleChange.ToString("F1")).Append("° \t");
            builder.Append("状态").Append(":").Append(cadence > 0 ? "旋转中" : "等待检测").Append("\n");
            return builder.ToString();
        }

        /// <summary>
        /// 更新踏频计算 - 基于累积角度的方法
        /// Update cadence calculation - based on accumulated angle
        /// </summary>
        private void UpdateCadenceCalculation(Bwt901ble device)
        {
            if (device == null) return;

            try
            {
                double currentAngleZ = Convert.ToDouble(device.GetDeviceData(WitSensorKey.AngleZ));
                long currentTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                // 初始化
                if (lastTime == 0)
                {
                    lastTime = currentTime;
                    lastAngleZ = currentAngleZ;
                    lastRecordedAngle = currentAngleZ;
                    return;
                }

                // 计算角度变化（处理360度边界）
                double angleDiff = currentAngleZ - lastAngleZ;

                // 处理角度跨越360度边界的情况
                if (angleDiff > 180)
                {
                    angleDiff -= 360;
                }
                else if (angleDiff < -180)
                {
                    angleDiff += 360;
                }

                // 累积角度变化（取绝对值，因为我们关心总的移动距离）
                totalAngleChange += Math.Abs(angleDiff);

                // 每累积360度算一次完整旋转
                if (totalAngleChange >= 360)
                {
                    rotationCount++;

                    // 记录旋转时间并计算踏频
                    if (lastRotationTime > 0)
                    {
                        long timeDiff = currentTime - lastRotationTime;
                        rotationTimes.Add(timeDiff);

                        // 只保留最近5次的时间记录
                        if (rotationTimes.Count > 5)
                        {
                            rotationTimes.RemoveAt(0);
                        }

                        // 计算平均踏频
                        if (rotationTimes.Count >= 1)
                        {
                            long totalTime = 0;
                            foreach (long time in rotationTimes)
                            {
                                totalTime += time;
                            }
                            double avgTime = totalTime / (double)rotationTimes.Count;
                            cadence = 60000.0 / avgTime; // 转换为RPM

                            Debug.WriteLine($"检测到完整旋转! 计数: {rotationCount}, 时间间隔: {timeDiff}ms, 踏频: {cadence:F1} RPM");
                        }
                    }
                    else
                    {
                        // 第一次检测到旋转
                        Debug.WriteLine($"第一次检测到完整旋转! 计数: {rotationCount}");
                    }

                    // 重置累积角度（减去360度，保留余数）
                    totalAngleChange -= 360;
                    lastRotationTime = currentTime;
                }

                lastAngleZ = currentAngleZ;
                lastTime = currentTime;

                // 如果没有检测到旋转，踏频逐渐衰减
                if (currentTime - lastRotationTime > 3000 && cadence > 0)
                {
                    cadence *= 0.95; // 缓慢衰减
                    if (cadence < 0.5)
                    {
                        cadence = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"踏频计算错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新踏频UI显示
        /// Update cadence UI display
        /// </summary>
        private void UpdateCadenceUI()
        {
            while (EnableRefreshDataTh)
            {
                Thread.Sleep(500); // 每500ms更新一次UI

                try
                {
                    if (this.InvokeRequired)
                    {
                        this.Invoke(new Action(() =>
                        {
                            UpdateCadenceControls();
                        }));
                    }
                    else
                    {
                        UpdateCadenceControls();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"UI更新错误: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 更新踏频控件显示
        /// Update cadence controls display
        /// </summary>
        private void UpdateCadenceControls()
        {
            try
            {
                // 更新踏频显示
                cadenceLabel.Text = $"踏频: {cadence:F1} RPM";
                rotationCountLabel.Text = $"旋转计数: {rotationCount}";

                // 如果有连接的设备，显示当前角度
                if (FoundDeviceDict.Count > 0)
                {
                    var firstDevice = FoundDeviceDict.Values.First();
                    if (firstDevice.IsOpen())
                    {
                        double currentAngle = Convert.ToDouble(firstDevice.GetDeviceData(WitSensorKey.AngleZ));
                        currentAngleLabel.Text = $"当前角度: {currentAngle:F1}°";
                        accumulatedAngleLabel.Text = $"累积角度: {totalAngleChange:F1}°";
                    }
                }

                // 更新状态
                if (cadence > 0)
                {
                    statusLabel.Text = "状态: 检测到旋转中...";
                    statusLabel.ForeColor = Color.Blue;
                }
                else
                {
                    statusLabel.Text = "状态: 等待检测旋转...";
                    statusLabel.ForeColor = Color.Green;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"控件更新错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 重置踏频计算
        /// Reset cadence calculation
        /// </summary>
        private void ResetCadenceCalculation(object sender, EventArgs e)
        {
            rotationCount = 0;
            cadence = 0;
            totalAngleChange = 0;
            rotationTimes.Clear();
            lastTime = 0;
            lastRotationTime = 0;
            lastRecordedAngle = 0;

            MessageBox.Show("踏频计数已重置", "重置", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// 加计校准
        /// Acceleration calibration
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void appliedCalibrationButton_Click(object sender, EventArgs e)
        {
            // 所有连接的蓝牙设备都加计校准
            // All connected Bluetooth devices are calibrated
            foreach (var bWT901BLE in FoundDeviceDict.Values)
            {
                if (bWT901BLE.IsOpen() == false)
                {
                    return;
                }

                try
                {
                    // 解锁寄存器并发送命令
                    // Unlock register and send command
                    bWT901BLE.UnlockReg();
                    bWT901BLE.AppliedCalibration();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
        }

        /// <summary>
        /// 读取03寄存器
        /// Read 03 register
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void readReg03Button_Click(object sender, EventArgs e)
        {
            string reg03Value = "";
            // 读取所有连接的蓝牙设备的03寄存器
            // Read the 03 register of all connected Bluetooth devices
            foreach (var bWT901BLE in FoundDeviceDict.Values)
            {
                if (bWT901BLE.IsOpen() == false)
                {
                    return;
                }
                try
                {
                    // 等待时长
                    // Waiting time
                    int waitTime = 3000;
                    // 发送读取命令，并且等待传感器返回数据，如果没读上来可以将 waitTime 延长，或者多读几次
                    // Send a read command and wait for the sensor to return data. If it is not read, the waitTime can be extended or read several more times
                    bWT901BLE.SendReadReg(0x03, waitTime);

                    // 拿到所有连接的蓝牙设备的值
                    // Get the values of all connected Bluetooth devices
                    reg03Value += bWT901BLE.GetDeviceName() + "的寄存器03值为 :" + bWT901BLE.GetDeviceData(new ShortKey("03")) + "\r\n";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
            MessageBox.Show(reg03Value);
        }

        /// <summary>
        /// 设置回传速率10Hz
        /// Set the return rate to 10Hz
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void returnRate10_Click(object sender, EventArgs e)
        {
            foreach (var bWT901BLE in FoundDeviceDict.Values)
            {
                if (bWT901BLE.IsOpen() == false)
                {
                    return;
                }
                try
                {
                    // 解锁寄存器并发送命令
                    // Unlock register and send command
                    bWT901BLE.UnlockReg();
                    bWT901BLE.SetReturnRate(0x06);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
        }

        /// <summary>
        /// 设置回传速率50Hz
        /// Set the return rate to 50Hz
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void returnRate50_Click(object sender, EventArgs e)
        {
            foreach (var bWT901BLE in FoundDeviceDict.Values)
            {
                if (bWT901BLE.IsOpen() == false)
                {
                    return;
                }
                try
                {
                    // 解锁寄存器并发送命令
                    // Unlock register and send command
                    bWT901BLE.UnlockReg();
                    bWT901BLE.SetReturnRate(0x08);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
        }

        /// <summary>
        /// 设置带宽20Hz
        /// Set bandwidth of 20Hz
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void bandWidth20_Click(object sender, EventArgs e)
        {
            foreach (var bWT901BLE in FoundDeviceDict.Values)
            {
                if (bWT901BLE.IsOpen() == false)
                {
                    return;
                }
                try
                {
                    // 解锁寄存器并发送命令
                    // Unlock register and send command
                    bWT901BLE.UnlockReg();
                    bWT901BLE.SetBandWidth(0x04);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
        }

        /// <summary>
        /// 设置带宽256Hz
        /// Set bandwidth of 256Hz
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void bandWidth256_Click(object sender, EventArgs e)
        {
            foreach (var bWT901BLE in FoundDeviceDict.Values)
            {
                if (bWT901BLE.IsOpen() == false)
                {
                    return;
                }
                try
                {
                    // 解锁寄存器并发送命令
                    // Unlock register and send command
                    bWT901BLE.UnlockReg();
                    bWT901BLE.SetBandWidth(0x00);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
        }

        /// <summary>
        /// 开始磁场校准
        /// Start magnetic field calibration
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void startFieldCalibrationButton_Click(object sender, EventArgs e)
        {
            // 开始所有连接的蓝牙设备的磁场校准
            // Start magnetic field calibration for all connected Bluetooth devices
            foreach (var bWT901BLE in FoundDeviceDict.Values)
            {
                if (bWT901BLE.IsOpen() == false)
                {
                    return;
                }
                try
                {
                    // 解锁寄存器并发送命令
                    // Unlock register and send command
                    bWT901BLE.UnlockReg();
                    bWT901BLE.StartFieldCalibration();
                    MessageBox.Show("开始磁场校准,请绕传感器XYZ三轴各转一圈,转完以后点击【结束磁场校准】");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
        }

        /// <summary>
        /// 结束磁场校准
        /// End magnetic field calibration
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void endFieldCalibrationButton_Click(object sender, EventArgs e)
        {
            // 结束所有连接的蓝牙设备的磁场校准
            // End the magnetic field calibration of all connected Bluetooth devices
            foreach (var bWT901BLE in FoundDeviceDict.Values)
            {
                if (bWT901BLE.IsOpen() == false)
                {
                    return;
                }
                try
                {
                    // 解锁寄存器并发送命令
                    // Unlock register and send command
                    bWT901BLE.UnlockReg();
                    bWT901BLE.EndFieldCalibration();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
        }
    }
}