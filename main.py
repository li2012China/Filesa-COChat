import socket
import threading
import tkinter as tk
from tkinter import simpledialog, scrolledtext, messagebox, ttk, font
import ipaddress
import time
import queue
import logging
import random
from datetime import datetime
import os
from PIL import ImageTk, Image
import traceback

class PopupNotification:
    """简化的弹窗通知类，用于显示聊天消息"""
    THEMES = {
        'Light': {
            'bg': '#FFFFFF',
            'fg_title': '#333333',
            'fg_msg': '#666666',
            'fg_time': '#999999'
        }
    }

    def __init__(self, root):
        # 获取程序所在目录
        self.base_dir = os.path.dirname(os.path.abspath(__file__))
        
        self.message_count = 0
        self.active_popups = []
        self.root = root  # 使用现有的root窗口，避免创建新的Tk实例
        self.max_messages = 5
        self.current_theme = 'Light'
        self.app_icons = {}  # 缓存应用图标
        
        # 启动定期刷新弹窗位置的定时器
        self.start_position_refresh()
        
        # 预加载主题图标
        self.load_theme_icons()

    def load_theme_icons(self):
        """预加载当前主题的所有应用图标"""
        # 图标现在存放在image目录下
        icon_dir = os.path.join(self.base_dir, 'image')
        print(f"[DEBUG] 正在从目录加载图标: {icon_dir}")
        
        # 加载消息图标
        app = 'message'
        icon_name = f"{app}-{self.current_theme}.png"
        icon_path = os.path.join(icon_dir, icon_name)
        
        try:
            img = Image.open(icon_path).resize((40, 40))
            self.app_icons[app] = ImageTk.PhotoImage(img)
            print(f"[DEBUG] 成功加载图标: {icon_name}")
        except Exception as e:
            print(f"[ERROR] 加载图标失败: {e}")
            self.app_icons[app] = self.create_default_icon(app)
        
        # 加载join和left图标
        for icon_type in ['join', 'left']:
            icon_name = f"{icon_type}.png"
            icon_path = os.path.join(icon_dir, icon_name)
            
            try:
                img = Image.open(icon_path).resize((40, 40))
                self.app_icons[icon_type] = ImageTk.PhotoImage(img)
                print(f"[DEBUG] 成功加载图标: {icon_name}")
            except Exception as e:
                print(f"[ERROR] 加载图标失败: {e}")
                # 如果加载失败，使用默认图标
                self.app_icons[icon_type] = self.create_default_icon(icon_type)

    def create_default_icon(self, app_type):
        """创建默认图标"""
        color = '#4CAF50'  # 绿色
        
        # 创建一个简单的带文字的默认图标
        img = Image.new('RGB', (40, 40), color=color)
        return ImageTk.PhotoImage(img)

    def get_app_icon(self, app_type):
        """获取应用图标"""
        return self.app_icons.get(app_type, self.create_default_icon('message'))

    def create_popup(self, title, message, icon_type='message'):
        """创建弹窗"""
        popup = tk.Toplevel(self.root)
        popup.overrideredirect(True)
        popup.attributes('-alpha', 0)
        popup.attributes('-topmost', True)
        popup.attributes('-transparentcolor', '#f0f0f0')
        popup.geometry("320x80+1000+100")

        theme = self.THEMES[self.current_theme]
        frame = tk.Frame(popup, bg=theme['bg'], bd=0, highlightthickness=0)
        frame.place(x=0, y=0, width=320, height=80)

        # 应用图标
        icon_img = self.get_app_icon(icon_type)
        icon_label = tk.Label(frame, image=icon_img, bg=theme['bg'])
        icon_label.image = icon_img
        icon_label.place(x=15, y=20)

        # 应用标题
        title_label = tk.Label(frame, text=title, bg=theme['bg'], 
                             fg=theme['fg_title'], font=('Microsoft YaHei', 12, 'bold'), 
                             anchor='w')
        title_label.place(x=70, y=15)

        # 消息内容
        msg_label = tk.Label(frame, text=message, bg=theme['bg'], 
                           fg=theme['fg_msg'], font=('Microsoft YaHei', 10))
        msg_label.place(x=70, y=40)

        # 时间
        time_str = datetime.now().strftime("%H:%M")
        time_label = tk.Label(frame, text=time_str, bg=theme['bg'], 
                            fg=theme['fg_time'], font=('Microsoft YaHei', 8))
        time_label.place(x=250, y=15)

        # 添加细边框增强视觉效果
        border_frame = tk.Frame(popup, bg='#DDDDDD', bd=0)
        border_frame.place(x=0, y=0, width=320, height=80, relwidth=1, relheight=1)
        frame.lift()

        # 添加点击事件，点击弹窗时将主窗口显示在最前面并关闭弹窗
        def on_popup_click(event):
            # 激活主窗口
            self.root.deiconify()
            self.root.lift()
            self.root.focus_force()
            
            # 查找弹窗在列表中的索引
            popup_index = -1
            for i, (p, _) in enumerate(self.active_popups):
                if p == popup:
                    popup_index = i
                    break
            
            # 关闭弹窗并重新排列
            if popup_index != -1:
                popup.destroy()
                self.reposition_after_hide(popup_index)

        # 为弹窗的各个组件添加点击事件
        for widget in [popup, frame, icon_label, title_label, msg_label, time_label]:
            widget.bind('<Button-1>', on_popup_click)

        return popup, frame

    def show_animation(self, popup, index):
        """显示弹窗动画"""
        screen_width = self.root.winfo_screenwidth()
        target_x = screen_width - 340
        base_y = 20
        spacing = 85

        popup.geometry(f"320x80+{screen_width}+{base_y + index * spacing}")
        popup.deiconify()

        start_time = datetime.now().timestamp()
        duration = 250  # 动画持续时间(毫秒)

        def animate():
            elapsed = (datetime.now().timestamp() - start_time) * 1000
            progress = min(elapsed / duration, 1.0)
            
            # 平滑移动动画
            current_x = int(screen_width - (screen_width - target_x) * progress)
            popup.geometry(f"320x80+{current_x}+{base_y + index * spacing}")
            popup.attributes('-alpha', progress)

            # 挤压下方消息效果
            if index > 0:
                for i in range(index):
                    offset = int(5 * (1 - progress))
                    if i < len(self.active_popups):
                        self.active_popups[i][0].geometry(
                            f"320x80+{target_x}+{base_y + i * spacing + offset}"
                        )

            if progress < 1.0:
                popup.after(16, animate)
            else:
                # 5秒后开始隐藏动画
                popup.after(5000, lambda: self.hide_animation(popup, index))

        animate()

    def hide_animation(self, popup, index):
        """隐藏弹窗动画"""
        screen_width = self.root.winfo_screenwidth()
        base_y = 20
        spacing = 85
        start_x = screen_width - 340

        start_time = datetime.now().timestamp()
        duration = 250  # 动画持续时间(毫秒)

        def animate():
            elapsed = (datetime.now().timestamp() - start_time) * 1000
            progress = min(elapsed / duration, 1.0)
            
            # 向右滑出动画
            current_x = int(start_x + 320 * progress)
            popup.geometry(f"320x80+{current_x}+{base_y + index * spacing}")
            popup.attributes('-alpha', 1 - progress)

            # 下方消息上移效果
            if index < len(self.active_popups) - 1:
                for i in range(index + 1, len(self.active_popups)):
                    offset = int(5 * progress)
                    if i < len(self.active_popups):
                        self.active_popups[i][0].geometry(
                            f"320x80+{screen_width - 340}+{base_y + i * spacing - offset}"
                        )

            if progress < 1.0:
                popup.after(16, animate)
            else:
                popup.destroy()
                self.reposition_after_hide(index)

        animate()

    def reposition_after_hide(self, removed_index):
        """消息消失后重新排列剩余消息"""
        screen_width = self.root.winfo_screenwidth()
        base_y = 20
        spacing = 85
        
        # 过滤掉已销毁的弹窗
        self.active_popups = [p for p in self.active_popups if p[0].winfo_exists()]
        
        # 重新定位所有消息
        for i, (popup, _) in enumerate(self.active_popups):
            target_y = base_y + i * spacing
            popup.geometry(f"320x80+{screen_width - 340}+{target_y}")
    
    def refresh_popup_positions(self):
        """刷新所有弹窗的位置"""
        screen_width = self.root.winfo_screenwidth()
        base_y = 20
        spacing = 85
        
        # 过滤掉已销毁的弹窗
        self.active_popups = [p for p in self.active_popups if p[0].winfo_exists()]
        
        # 重新定位所有消息
        for i, (popup, _) in enumerate(self.active_popups):
            target_y = base_y + i * spacing
            popup.geometry(f"320x80+{screen_width - 340}+{target_y}")
    
    def start_position_refresh(self):
        """启动定期刷新弹窗位置的定时器"""
        def refresh_timer():
            self.refresh_popup_positions()
            # 每200毫秒刷新一次
            self.root.after(200, refresh_timer)
        
        # 立即执行一次，然后启动定时器
        self.refresh_popup_positions()
        self.root.after(200, refresh_timer)

    def show_message(self, title, message, icon_type='message'):
        """显示弹窗消息"""
        self.message_count += 1
        
        # 移除最旧的消息如果达到上限
        if len(self.active_popups) >= self.max_messages:
            oldest_popup = self.active_popups.pop()
            oldest_popup[0].destroy()

        # 创建新弹窗
        popup, frame = self.create_popup(title, message, icon_type)
        self.active_popups.insert(0, (popup, frame))
        
        # 立即调整所有现有弹窗的位置
        screen_width = self.root.winfo_screenwidth()
        base_y = 20
        spacing = 85
        
        for i, (p, _) in enumerate(self.active_popups):
            if p != popup:  # 跳过新创建的弹窗，它会通过动画进入
                p.geometry(f"320x80+{screen_width - 340}+{base_y + i * spacing}")
        
        # 显示动画
        self.show_animation(popup, 0)

class FilesaCOChat:
    def __init__(self):
        # 获取程序所在目录
        self.base_dir = os.path.dirname(os.path.abspath(__file__))
        
        self.host = self.get_local_ip()
        self.port = 11451
        self.username = None
        self.server_socket = None
        self.client_sockets = []
        self.is_server = False
        self.running = True
        self.found_server = None
        self.scan_complete = threading.Event()
        
        # 用户列表相关
        self.users = {}  # 存储用户名到socket的映射
        self.user_list = []  # 存储当前在线用户
        
        # 线程安全锁
        self.user_list_lock = threading.Lock()
        self.client_sockets_lock = threading.Lock()
        
        # 字体设置
        self.chat_font = ("Arial", 10)
        self.user_list_font = ("Arial", 9)
        
        # 版本信息
        self.version = "4.0"
        
        # 用于线程间通信的队列
        self.message_queue = queue.Queue()
        
        # 服务器迁移相关
        self.migrating = False
        self.new_server_ip = None
        
        # 日志记录
        logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
        self.logger = logging.getLogger("FilesaCOChat")
        
        # GUI初始化
        self.root = tk.Tk()
        self.root.title("Filesa COChat")
        self.root.geometry("700x500")  # 增加宽度以容纳用户列表
        self.root.resizable(True, True)
        
        # 初始化弹窗通知系统
        self.popup_notifier = PopupNotification(self.root)
        
        # 创建主框架
        self.main_frame = tk.Frame(self.root)
        self.main_frame.pack(fill=tk.BOTH, expand=True, padx=5, pady=5)
        
        # 创建进度条框架（初始显示）
        self.progress_frame = tk.Frame(self.main_frame)
        self.progress_label = tk.Label(self.progress_frame, text="正在搜索局域网聊天室...")
        self.progress_label.pack(pady=5)
        
        self.progress_bar = ttk.Progressbar(
            self.progress_frame, 
            orient=tk.HORIZONTAL, 
            length=300, 
            mode='indeterminate'
        )
        self.progress_bar.pack(pady=5, padx=10, fill=tk.X)
        
        self.progress_status = tk.Label(self.progress_frame, text="准备开始搜索...")
        self.progress_status.pack(pady=5)
        
        # 创建聊天界面框架（初始隐藏）
        self.chat_frame = tk.Frame(self.main_frame)
        
        # 工具栏
        self.toolbar_frame = tk.Frame(self.chat_frame)
        self.toolbar_frame.pack(fill=tk.X, padx=5, pady=2)
        
        # 添加设置按钮
        self.settings_button = tk.Button(
            self.toolbar_frame, 
            text="设置", 
            command=self.open_settings,
            width=5
        )
        self.settings_button.pack(side=tk.RIGHT, padx=5)
        
        # 左侧聊天区域
        self.chat_area_frame = tk.Frame(self.chat_frame)
        self.chat_area_frame.pack(side=tk.LEFT, fill=tk.BOTH, expand=True, padx=(0, 5))
        
        self.chat_area = scrolledtext.ScrolledText(
            self.chat_area_frame, 
            wrap=tk.WORD, 
            state='disabled',
            font=self.chat_font
        )
        self.chat_area.pack(fill=tk.BOTH, expand=True, padx=10, pady=10)
        
        # 输入区域
        input_frame = tk.Frame(self.chat_area_frame)
        input_frame.pack(fill=tk.X, padx=10, pady=5)
        
        self.message_entry = tk.Entry(input_frame, font=self.chat_font)
        self.message_entry.pack(side=tk.LEFT, fill=tk.X, expand=True)
        self.message_entry.bind('<Return>', self.send_message)
        
        self.send_button = tk.Button(input_frame, text="发送", command=self.send_message)
        self.send_button.pack(side=tk.RIGHT, padx=(5, 0))
        
        # 右侧用户列表区域
        self.user_frame = tk.Frame(self.chat_frame, width=150)
        self.user_frame.pack(side=tk.RIGHT, fill=tk.Y, padx=(5, 0))
        self.user_frame.pack_propagate(False)  # 防止框架收缩
        
        # 用户列表标题
        user_title_frame = tk.Frame(self.user_frame, bg="#f0f0f0", height=30)
        user_title_frame.pack(fill=tk.X)
        user_title_frame.pack_propagate(False)
        
        user_title = tk.Label(
            user_title_frame, 
            text="在线用户", 
            bg="#f0f0f0", 
            fg="#333", 
            font=("Arial", 10, "bold")
        )
        user_title.pack(expand=True)
        
        # 用户列表
        self.user_listbox = tk.Listbox(
            self.user_frame, 
            bg="#fafafa", 
            relief=tk.FLAT,
            font=self.user_list_font
        )
        self.user_listbox.pack(fill=tk.BOTH, expand=True, pady=(0, 5))
        
        # 用户计数
        self.user_count_label = tk.Label(
            self.user_frame, 
            text="用户: 0", 
            bg="#f0f0f0", 
            fg="#666",
            font=self.user_list_font
        )
        self.user_count_label.pack(fill=tk.X, side=tk.BOTTOM)
        
        # 创建登录界面框架
        self.login_frame = tk.Frame(self.main_frame)
        self.login_frame.pack(fill=tk.BOTH, expand=True, padx=20, pady=20)
        
        # 登录界面logo
        try:
            # 从image目录加载logo.png，保持原始比例
            logo_path = os.path.join(self.base_dir, 'image', 'logo.png')
            logo_image = Image.open(logo_path)
            
            # 获取原始尺寸
            original_width, original_height = logo_image.size
            
            # 计算合适的缩放比例，最大宽度和高度为200
            max_size = 200
            if original_width > original_height:
                # 按宽度缩放
                scale_factor = max_size / original_width
            else:
                # 按高度缩放
                scale_factor = max_size / original_height
            
            # 计算新尺寸，保持原始比例
            new_width = int(original_width * scale_factor)
            new_height = int(original_height * scale_factor)
            
            # 缩放图片
            logo_image = logo_image.resize((new_width, new_height))
            self.logo_photo = ImageTk.PhotoImage(logo_image)
            
            logo_label = tk.Label(
                self.login_frame, 
                image=self.logo_photo
            )
            logo_label.pack(pady=20)
        except Exception as e:
            print(f"[ERROR] 加载logo失败: {e}")
            # 如果加载失败，显示文字标题
            login_title = tk.Label(
                self.login_frame, 
                text="Filesa COChat", 
                font=('Microsoft YaHei', 24, 'bold')
            )
            login_title.pack(pady=30)
        
        # 用户名标签
        username_label = tk.Label(
            self.login_frame, 
            text="用户名:", 
            font=('Microsoft YaHei', 12)
        )
        username_label.pack(pady=10)
        
        # 用户名输入框
        self.username_entry = tk.Entry(
            self.login_frame, 
            font=('Microsoft YaHei', 12),
            width=30
        )
        self.username_entry.pack(pady=10)
        self.username_entry.bind('<Return>', self.login)
        
        # 登录按钮
        self.login_button = tk.Button(
            self.login_frame, 
            text="登录", 
            command=self.login,
            font=('Microsoft YaHei', 12),
            width=15,
            height=2
        )
        self.login_button.pack(pady=20)
        
        # 初始显示登录界面，隐藏其他界面
        self.login_frame.pack(fill=tk.BOTH, expand=True, padx=20, pady=20)
        self.progress_frame.pack_forget()
        self.chat_frame.pack_forget()
    
    def get_local_ip(self):
        """获取本机在局域网中的IP地址"""
        try:
            s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            s.connect(("8.8.8.8", 80))
            local_ip = s.getsockname()[0]
            s.close()
            return local_ip
        except Exception as e:
            self.logger.error(f"获取本地IP失败: {e}")
            return "127.0.0.1"
    
    def get_network_ips(self):
        """获取局域网内所有可能的IP地址"""
        try:
            network = ipaddress.IPv4Network(f"{self.host}/24", strict=False)
            return [str(ip) for ip in network.hosts()]
        except Exception as e:
            self.logger.error(f"获取网络IP失败: {e}")
            return []
    
    def check_port(self, ip, port, timeout=1):
        """检查指定IP的端口是否开放"""
        try:
            sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            sock.settimeout(timeout)
            result = sock.connect_ex((ip, port))
            sock.close()
            return result == 0
        except Exception as e:
            self.logger.debug(f"检查端口失败: {ip}:{port} - {e}")
            return False
    
    def update_progress(self, current, total, status):
        """更新进度条和状态"""
        if not self.running:
            return
            
        # 计算百分比
        percent = int((current / total) * 100) if total > 0 else 0
        self.progress_bar['value'] = percent
        self.progress_status.config(text=status)
        self.root.update_idletasks()
    
    def broadcast_discovery(self):
        """使用广播方式发现聊天室"""
        try:
            # 创建广播socket
            broadcast_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            broadcast_sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
            broadcast_sock.settimeout(2)
            
            # 发送广播消息
            broadcast_msg = f"COCHAT_DISCOVERY:{self.port}"
            broadcast_addr = ('255.255.255.255', 11452)  # 使用另一个端口进行发现
            
            broadcast_sock.sendto(broadcast_msg.encode(), broadcast_addr)
            self.logger.info("发送广播发现消息")
            
            # 等待响应
            start_time = time.time()
            while time.time() - start_time < 3:  # 等待3秒
                try:
                    data, addr = broadcast_sock.recvfrom(1024)
                    if data.decode() == f"COCHAT_RESPONSE:{self.port}":
                        self.logger.info(f"收到广播响应: {addr[0]}")
                        broadcast_sock.close()
                        return addr[0]  # 返回服务器的IP地址
                except socket.timeout:
                    continue
                except Exception as e:
                    self.logger.debug(f"广播接收异常: {e}")
                    continue
            
            broadcast_sock.close()
        except Exception as e:
            self.logger.error(f"广播发现失败: {e}")
        
        return None
    
    def threaded_port_scan(self, ip_list, timeout=1):
        """使用多线程并行扫描端口"""
        results = {}
        threads = []
        result_lock = threading.Lock()
        completed_ips = 0
        total_ips = len(ip_list)
        
        def check_single_ip(ip):
            nonlocal completed_ips
            if not self.running or self.found_server:
                return
                
            is_open = self.check_port(ip, self.port, timeout)
            
            with result_lock:
                results[ip] = is_open
                completed_ips += 1
                if is_open:
                    self.found_server = ip
                    self.scan_complete.set()
        
        # 创建并启动线程
        for ip in ip_list:
            if not self.running or self.found_server:
                break
                
            thread = threading.Thread(target=check_single_ip, args=(ip,))
            thread.daemon = True
            threads.append(thread)
            thread.start()
            
            # 控制并发线程数量，避免创建过多线程
            if len(threads) >= 50:  # 最大并发50个线程
                for t in threads:
                    t.join(timeout=0.1)
                threads = []
        
        # 等待所有线程完成
        for thread in threads:
            thread.join(timeout=0.1)
        
        return self.found_server
    
    def find_existing_chatroom(self):
        """使用高效方式搜索局域网内已有的聊天室"""
        self.update_progress(0, 100, "正在使用广播方式发现聊天室...")
        
        # 首先尝试广播发现（最快的方式）
        server_ip = self.broadcast_discovery()
        
        if server_ip:
            self.update_progress(100, 100, f"通过广播发现聊天室: {server_ip}")
            return server_ip
        
        # 如果广播失败，使用多线程扫描
        self.update_progress(10, 100, "广播未发现聊天室，开始扫描...")
        
        network_ips = self.get_network_ips()
        # 排除自己的IP
        network_ips = [ip for ip in network_ips if ip != self.host]
        total_ips = len(network_ips)
        
        if total_ips == 0:
            self.progress_bar.stop()
            self.update_progress(100, 100, "未找到可扫描的IP地址")
            return None
        
        # 重置发现状态
        self.found_server = None
        self.scan_complete.clear()
        
        # 启动进度更新线程
        progress_thread = threading.Thread(target=self.update_scan_progress, args=(total_ips,))
        progress_thread.daemon = True
        progress_thread.start()
        
        # 使用多线程扫描
        server_ip = self.threaded_port_scan(network_ips, timeout=0.5)
        
        # 等待扫描完成或超时
        self.scan_complete.wait(timeout=5)
        
        # 停止滑动动画
        self.progress_bar.stop()
        
        if server_ip:
            self.update_progress(100, 100, f"通过扫描发现聊天室: {server_ip}")
            return server_ip
        else:
            self.update_progress(100, 100, "扫描完成，未找到现有聊天室")
            return None
    
    def update_scan_progress(self, total_ips):
        """更新扫描进度（在多线程中运行）"""
        # 对于滑动进度条，只需要显示"请稍后"即可
        self.update_progress(0, 100, "请稍后")
        
        # 启动滑动动画
        self.progress_bar.start()
        
        checked_ips = 0
        
        while checked_ips < total_ips and self.running and not self.found_server:
            # 短暂延迟，避免过度更新
            time.sleep(0.1)
            checked_ips += 1
    
    def search_chatroom_thread(self):
        """在后台线程中搜索聊天室"""
        existing_server = self.find_existing_chatroom()
        
        # 将结果放入队列，供主线程处理
        self.message_queue.put(("search_result", existing_server))
    
    def join_chatroom(self, server_ip):
        """加入已存在的聊天室"""
        try:
            client_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            client_socket.connect((server_ip, self.port))
            # 发送用户名
            client_socket.send(f"JOIN:{self.username}".encode())
            
            with self.client_sockets_lock:
                self.client_sockets.append(client_socket)
            
            # 保存用户信息
            self.users[client_socket] = self.username
            
            # 启动接收消息的线程
            receive_thread = threading.Thread(target=self.receive_messages, args=(client_socket,))
            receive_thread.daemon = True
            receive_thread.start()
            
            self.add_message("系统", f"已成功加入 {server_ip} 的聊天室")
            
            # 请求用户列表
            client_socket.send("USERLIST_REQUEST".encode())
            
            return True
        except Exception as e:
            self.logger.error(f"加入聊天室失败: {server_ip} - {e}")
            self.add_message("系统", f"无法连接到 {server_ip} 的聊天室")
            return False
    
    def create_chatroom(self):
        """创建新的聊天室，并启动发现服务"""
        try:
            self.server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            self.server_socket.bind(('0.0.0.0', self.port))
            self.server_socket.listen(5)
            self.is_server = True
            
            # 将自己添加到用户列表
            with self.user_list_lock:
                self.user_list.append(self.username)
                self.update_user_list()
            
            # 广播用户列表（包含自己）
            self.broadcast_user_list()
            
            # 启动发现服务响应线程
            discovery_thread = threading.Thread(target=self.discovery_service)
            discovery_thread.daemon = True
            discovery_thread.start()
            
            self.add_message("系统", f"已在 {self.host}:{self.port} 创建新的聊天室")
            
            # 启动接受连接的线程
            accept_thread = threading.Thread(target=self.accept_connections)
            accept_thread.daemon = True
            accept_thread.start()
            
            return True
        except Exception as e:
            self.logger.error(f"创建聊天室失败: {e}")
            self.add_message("系统", "创建聊天室失败")
            return False
    
    def discovery_service(self):
        """发现服务，响应广播发现请求"""
        try:
            discovery_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            discovery_sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            discovery_sock.bind(('0.0.0.0', 11452))  # 监听发现端口
            
            while self.running and self.is_server:
                try:
                    data, addr = discovery_sock.recvfrom(1024)
                    if data.decode().startswith("COCHAT_DISCOVERY:"):
                        # 收到发现请求，发送响应
                        response = f"COCHAT_RESPONSE:{self.port}"
                        discovery_sock.sendto(response.encode(), addr)
                        self.logger.info(f"响应发现请求: {addr[0]}")
                except socket.timeout:
                    continue
                except Exception as e:
                    self.logger.debug(f"发现服务异常: {e}")
                    continue
        except Exception as e:
            self.logger.error(f"发现服务失败: {e}")
    
    def accept_connections(self):
        """接受客户端连接（仅服务器运行）"""
        while self.running and self.is_server:
            try:
                client_socket, addr = self.server_socket.accept()
                
                with self.client_sockets_lock:
                    self.client_sockets.append(client_socket)
                
                # 启动接收消息的线程
                receive_thread = threading.Thread(target=self.receive_messages, args=(client_socket,))
                receive_thread.daemon = True
                receive_thread.start()
                
                self.add_message("系统", f"{addr[0]} 正在连接...")
            except Exception as e:
                self.logger.debug(f"接受连接异常: {e}")
                break
    
    def receive_messages(self, client_socket):
        """接收来自其他客户端的消息"""
        while self.running:
            try:
                message = client_socket.recv(1024).decode()
                if not message:
                    break
                
                if message.startswith("JOIN:"):
                    # 新用户加入
                    username = message[5:]
                    
                    # 检查用户名是否已存在
                    if self.is_server and username in self.user_list:
                        client_socket.send("USERNAME_EXISTS".encode())
                        self.logger.warning(f"用户名冲突: {username}")
                        continue
                    
                    self.users[client_socket] = username
                    
                    # 如果是服务器，更新用户列表并广播
                    if self.is_server:
                        with self.user_list_lock:
                            if username not in self.user_list:
                                self.user_list.append(username)
                                self.update_user_list()
                                self.broadcast_user_list()
                        
                        self.add_message("系统", f"{username} 加入了聊天室")
                
                elif message == "USERNAME_EXISTS":
                    # 用户名冲突
                    self.add_message("系统", "用户名已存在，请重新启动并选择其他用户名")
                    self.logger.error("用户名冲突，无法加入聊天室")
                    break
                
                elif message == "USERLIST_REQUEST":
                    # 客户端请求用户列表
                    if self.is_server:
                        self.send_user_list(client_socket)
                
                elif message.startswith("USERLIST:"):
                    # 收到用户列表更新
                    if not self.is_server:  # 只有客户端需要处理
                        user_list_str = message[9:]  # 去掉"USERLIST:"前缀
                        with self.user_list_lock:
                            self.user_list = user_list_str.split(",") if user_list_str else []
                            self.update_user_list()
                
                elif message.startswith("MIGRATE:"):
                    # 服务器迁移消息
                    self.handle_migration(message)
                
                elif message.startswith("PROMOTE:"):
                    # 被提升为新的服务器
                    self.handle_promotion(message)
                
                else:
                    # 普通聊天消息
                    parts = message.split(":", 1)
                    if len(parts) == 2:
                        username, msg = parts
                        self.add_message(username, msg)
            except Exception as e:
                self.logger.debug(f"接收消息异常: {e}")
                break
        
        # 连接断开处理
        if client_socket in self.users:
            username = self.users[client_socket]
            
            # 如果是服务器，从用户列表中移除并广播更新
            if self.is_server and username in self.user_list:
                with self.user_list_lock:
                    self.user_list.remove(username)
                    self.update_user_list()
                    self.broadcast_user_list()
            
            del self.users[client_socket]
            self.add_message("系统", f"{username} 离开了聊天室")
        
        # 关闭连接
        try:
            client_socket.close()
        except:
            pass
        
        with self.client_sockets_lock:
            if client_socket in self.client_sockets:
                self.client_sockets.remove(client_socket)
    
    def handle_migration(self, message):
        """处理服务器迁移消息"""
        if self.is_server:
            # 如果自己已经是服务器，忽略迁移消息
            return
            
        parts = message.split(":")
        if len(parts) >= 2:
            new_server_ip = parts[1]
            self.logger.info(f"收到迁移消息，新服务器IP: {new_server_ip}")
            
            # 如果迁移目标是自己，忽略
            if new_server_ip == self.host:
                return
                
            self.migrating = True
            self.new_server_ip = new_server_ip
            
            # 关闭当前所有连接
            self.disconnect_all()
            
            # 尝试连接新的服务器
            self.add_message("系统", f"迁移到新的服务器 {new_server_ip}...")
            if self.join_chatroom(new_server_ip):
                self.add_message("系统", "迁移成功")
            else:
                self.add_message("系统", "迁移失败，尝试重新连接...")
                # 如果迁移失败，尝试重新连接
                self.reconnect_to_server()
    
    def handle_promotion(self, message):
        """处理被提升为服务器的消息"""
        if self.is_server:
            # 如果自己已经是服务器，忽略提升消息
            return
            
        parts = message.split(":")
        if len(parts) >= 2:
            self.logger.info(f"被提升为新的服务器")
            self.add_message("系统", "被指定为新的服务器，正在创建聊天室...")
            
            # 关闭当前所有连接
            self.disconnect_all()
            
            # 创建新的聊天室
            if self.create_chatroom():
                self.add_message("系统", "已成功创建新的聊天室")
            else:
                self.add_message("系统", "创建聊天室失败")
    
    def disconnect_all(self):
        """断开所有连接"""
        with self.client_sockets_lock:
            for client_socket in self.client_sockets:
                try:
                    client_socket.close()
                except:
                    pass
            self.client_sockets = []
        
        # 清空用户列表
        with self.user_list_lock:
            self.user_list = []
        self.update_user_list()
    
    def reconnect_to_server(self):
        """尝试重新连接到服务器"""
        if self.new_server_ip:
            self.add_message("系统", f"尝试重新连接到服务器 {self.new_server_ip}...")
            if self.join_chatroom(self.new_server_ip):
                self.add_message("系统", "重新连接成功")
            else:
                # 如果重新连接失败，等待一段时间后重试
                self.root.after(5000, self.reconnect_to_server)
    
    def select_new_server(self):
        """选择一个新的服务器"""
        if not self.user_list or len(self.user_list) == 1:
            return None
            
        # 排除自己
        candidates = [user for user in self.user_list if user != self.username]
        
        if not candidates:
            return None
            
        # 随机选择一个用户作为新的服务器
        return random.choice(candidates)
    
    def initiate_migration(self):
        """启动服务器迁移过程"""
        if not self.is_server:
            return
            
        # 选择一个新的服务器
        new_server_username = self.select_new_server()
        if not new_server_username:
            self.add_message("系统", "没有其他用户可迁移服务器")
            return False
        
        self.add_message("系统", f"正在迁移服务器到 {new_server_username}...")
        
        # 找到新服务器的socket
        new_server_socket = None
        for sock, username in self.users.items():
            if username == new_server_username:
                new_server_socket = sock
                break
        
        if not new_server_socket:
            self.add_message("系统", "无法找到新服务器用户")
            return False
        
        # 获取新服务器的IP地址
        new_server_ip = None
        try:
            # 获取socket的远程地址
            new_server_ip = new_server_socket.getpeername()[0]
        except:
            pass
        
        if not new_server_ip:
            self.add_message("系统", "无法获取新服务器的IP地址")
            return False
        
        # 发送提升消息给新服务器
        try:
            promote_msg = f"PROMOTE:{self.host}"
            new_server_socket.send(promote_msg.encode())
        except:
            self.add_message("系统", "发送提升消息失败")
            return False
        
        # 广播迁移消息给所有客户端
        migrate_msg = f"MIGRATE:{new_server_ip}"
        disconnected_clients = []
        
        with self.client_sockets_lock:
            for client_socket in self.client_sockets:
                try:
                    client_socket.send(migrate_msg.encode())
                except:
                    disconnected_clients.append(client_socket)
        
        # 移除断开连接的客户端
        for client in disconnected_clients:
            if client in self.client_sockets:
                with self.client_sockets_lock:
                    self.client_sockets.remove(client)
        
        # 等待消息发送完成
        time.sleep(1)
        
        return True
    
    def update_user_list(self):
        """更新用户列表显示"""
        if not self.running:
            return
            
        # 在主线程中更新UI
        self.root.after(0, self._update_user_list_ui)
    
    def _update_user_list_ui(self):
        """在主线程中更新用户列表UI"""
        self.user_listbox.delete(0, tk.END)
        
        for user in self.user_list:
            self.user_listbox.insert(tk.END, user)
        
        # 更新用户计数
        self.user_count_label.config(text=f"用户: {len(self.user_list)}")
    
    def broadcast_user_list(self):
        """广播用户列表给所有客户端（仅服务器调用）"""
        if not self.is_server:
            return
            
        # 确保包含服务器用户名
        if self.username not in self.user_list:
            with self.user_list_lock:
                self.user_list.append(self.username)
        
        user_list_str = ",".join(self.user_list)
        message = f"USERLIST:{user_list_str}"
        
        disconnected_clients = []
        with self.client_sockets_lock:
            for client_socket in self.client_sockets:
                try:
                    client_socket.send(message.encode())
                except:
                    disconnected_clients.append(client_socket)
        
        # 移除断开连接的客户端
        for client in disconnected_clients:
            if client in self.client_sockets:
                with self.client_sockets_lock:
                    self.client_sockets.remove(client)
    
    def send_user_list(self, client_socket):
        """发送用户列表给特定客户端（仅服务器调用）"""
        if not self.is_server:
            return
            
        # 确保包含服务器用户名
        if self.username not in self.user_list:
            with self.user_list_lock:
                self.user_list.append(self.username)
        
        user_list_str = ",".join(self.user_list)
        message = f"USERLIST:{user_list_str}"
        
        try:
            client_socket.send(message.encode())
        except Exception as e:
            self.logger.debug(f"发送用户列表失败: {e}")
    
    def send_message(self, event=None):
        """发送消息给所有连接的客户端"""
        message = self.message_entry.get().strip()
        if not message:
            return
        
        # 清空输入框
        self.message_entry.delete(0, tk.END)
        
        # 显示自己的消息
        self.add_message(self.username, message)
        
        # 发送给所有连接的客户端
        full_message = f"{self.username}:{message}"
        disconnected_clients = []
        
        with self.client_sockets_lock:
            for client_socket in self.client_sockets:
                try:
                    client_socket.send(full_message.encode())
                except:
                    disconnected_clients.append(client_socket)
        
        # 移除断开连接的客户端
        for client in disconnected_clients:
            if client in self.client_sockets:
                with self.client_sockets_lock:
                    self.client_sockets.remove(client)
    
    def add_message(self, username, message):
        """在聊天区域添加消息"""
        self.chat_area.config(state='normal')
        self.chat_area.insert(tk.END, f"[{username}] {message}\n")
        self.chat_area.config(state='disabled')
        self.chat_area.see(tk.END)
        
        # 显示弹窗通知
        if username != self.username:
            # 限制消息长度，避免弹窗显示不全
            display_message = message[:30] + "..." if len(message) > 30 else message
            
            # 根据消息内容选择合适的图标
            icon_type = 'message'
            if "加入了聊天室" in message:
                icon_type = 'join'
            elif "离开了聊天室" in message:
                icon_type = 'left'
            
            self.popup_notifier.show_message(username, display_message, icon_type)
    
    def open_settings(self):
        """打开设置窗口"""
        settings_window = tk.Toplevel(self.root)
        settings_window.title("设置")
        settings_window.geometry("600x400")
        settings_window.resizable(True, True)
        settings_window.transient(self.root)  # 设置为模态窗口
        settings_window.grab_set()  # 捕获所有事件
        
        # 创建选项卡
        tab_control = ttk.Notebook(settings_window)
        
        # 字体设置选项卡
        font_tab = ttk.Frame(tab_control)
        tab_control.add(font_tab, text='字体设置')
        
        # 关于选项卡
        about_tab = ttk.Frame(tab_control)
        tab_control.add(about_tab, text='关于')
        
        # 鸣谢选项卡
        thanks_tab = ttk.Frame(tab_control)
        tab_control.add(thanks_tab, text='鸣谢')
        
        tab_control.pack(expand=1, fill="both", padx=10, pady=10)
        
        # 字体设置内容
        font_frame = ttk.LabelFrame(font_tab, text="聊天区域字体")
        font_frame.pack(fill="both", expand=True, padx=10, pady=10)
        
        # 字体选择
        font_label = ttk.Label(font_frame, text="选择字体:")
        font_label.grid(row=0, column=0, padx=5, pady=5, sticky="w")
        
        # 获取可用字体
        available_fonts = list(font.families())
        available_fonts.sort()
        
        self.font_var = tk.StringVar(value=self.chat_font[0])
        font_combo = ttk.Combobox(
            font_frame, 
            textvariable=self.font_var, 
            values=available_fonts,
            state="readonly",
            width=30
        )
        font_combo.grid(row=0, column=1, padx=5, pady=5, sticky="ew")
        
        # 字体大小
        size_label = ttk.Label(font_frame, text="字体大小:")
        size_label.grid(row=1, column=0, padx=5, pady=5, sticky="w")
        
        self.size_var = tk.IntVar(value=self.chat_font[1])
        size_spin = ttk.Spinbox(
            font_frame, 
            textvariable=self.size_var, 
            from_=8, 
            to=24,
            width=5
        )
        size_spin.grid(row=1, column=1, padx=5, pady=5, sticky="w")
        
        # 字体样式
        style_label = ttk.Label(font_frame, text="字体样式:")
        style_label.grid(row=2, column=0, padx=5, pady=5, sticky="w")
        
        self.bold_var = tk.BooleanVar(value=False)
        bold_check = ttk.Checkbutton(
            font_frame, 
            text="粗体", 
            variable=self.bold_var
        )
        bold_check.grid(row=2, column=1, padx=5, pady=5, sticky="w")
        
        # 预览区域
        preview_label = ttk.Label(font_frame, text="预览:")
        preview_label.grid(row=3, column=0, padx=5, pady=5, sticky="w")
        
        preview_text = tk.Text(
            font_frame, 
            height=3, 
            width=30,
            state='disabled'
        )
        preview_text.grid(row=3, column=1, padx=5, pady=5, sticky="ew")
        
        # 更新预览
        def update_preview():
            font_name = self.font_var.get()
            font_size = self.size_var.get()
            font_weight = "bold" if self.bold_var.get() else "normal"
            
            preview_font = (font_name, font_size, font_weight)
            preview_text.config(state='normal')
            preview_text.delete(1.0, tk.END)
            preview_text.insert(tk.END, "这是一个字体预览示例")
            preview_text.config(font=preview_font, state='disabled')
        
        # 初始预览
        update_preview()
        
        # 绑定事件
        font_combo.bind("<<ComboboxSelected>>", lambda e: update_preview())
        size_spin.bind("<KeyRelease>", lambda e: update_preview())
        size_spin.bind("<ButtonRelease>", lambda e: update_preview())
        bold_check.bind("<ButtonRelease>", lambda e: update_preview())
        
        # 应用按钮
        apply_button = ttk.Button(
            font_frame, 
            text="应用字体设置", 
            command=lambda: self.apply_font_settings(
                self.font_var.get(),
                self.size_var.get(),
                self.bold_var.get()
            )
        )
        apply_button.grid(row=4, column=1, padx=5, pady=10, sticky="e")
        
        # 关于选项卡内容
        about_frame = ttk.LabelFrame(about_tab, text="软件信息")
        about_frame.pack(fill="both", expand=True, padx=10, pady=10)
        
        # 软件名称（可点击链接）
        name_label = ttk.Label(about_frame, text="软件名称:")
        name_label.grid(row=0, column=0, padx=10, pady=5, sticky="w")
        
        # 创建可点击的软件名称
        def open_github():
            import webbrowser
            webbrowser.open("https://github.com/li2012China/Filesa-COChat")
        
        name_value = ttk.Label(
            about_frame, 
            text="Filesa COChat",
            foreground="blue",
            cursor="hand2"
        )
        name_value.grid(row=0, column=1, padx=10, pady=5, sticky="w")
        name_value.bind("<Button-1>", lambda e: open_github())
        
        # 版本号
        version_label = ttk.Label(about_frame, text="版本号:")
        version_label.grid(row=1, column=0, padx=10, pady=5, sticky="w")
        
        version_value = ttk.Label(about_frame, text=self.version)
        version_value.grid(row=1, column=1, padx=10, pady=5, sticky="w")
        
        # 开发者（可点击链接）
        developer_label = ttk.Label(about_frame, text="开发者:")
        developer_label.grid(row=2, column=0, padx=10, pady=5, sticky="w")
        
        # 创建可点击的开发者名称
        def open_developer_github():
            import webbrowser
            webbrowser.open("https://github.com/li2012China")
        
        developer_value = ttk.Label(
            about_frame, 
            text="li2012China",
            foreground="blue",
            cursor="hand2"
        )
        developer_value.grid(row=2, column=1, padx=10, pady=5, sticky="w")
        developer_value.bind("<Button-1>", lambda e: open_developer_github())
        
        # 版权信息
        copyright_label = ttk.Label(about_frame, text="版权:")
        copyright_label.grid(row=3, column=0, padx=10, pady=5, sticky="w")
        
        # 使用当前年份
        current_year = datetime.now().year
        copyright_value = ttk.Label(about_frame, text=f"© {current_year} Filesa COChat. 保留所有权利。")
        copyright_value.grid(row=3, column=1, padx=10, pady=5, sticky="w")
        
        # 描述
        desc_frame = ttk.Frame(about_frame)
        desc_frame.grid(row=4, column=0, columnspan=2, padx=10, pady=10, sticky="ew")
        
        desc_label = ttk.Label(desc_frame, text="软件描述:")
        desc_label.pack(anchor="w")
        
        desc_text = tk.Text(
            desc_frame, 
            height=4, 
            width=40,
            wrap=tk.WORD,
            state='disabled'
        )
        desc_text.pack(fill="both", expand=True, pady=5)
        desc_text.config(state='normal')
        desc_text.insert(tk.END, "Filesa COChat 是一个基于局域网的多人实时聊天软件，支持个性化字体，最重要的是无需服务端！")
        desc_text.config(state='disabled')
    
        # 鸣谢选项卡内容
        thanks_frame = ttk.LabelFrame(thanks_tab, text="鸣谢")
        thanks_frame.pack(fill="both", expand=True, padx=10, pady=10)
        
        # 鸣谢信息
        thanks_label = ttk.Label(thanks_frame, text="感谢所有为 Filesa COChat 做出贡献的开发者和用户！")
        thanks_label.pack(padx=10, pady=10, anchor="w")
        
        thanks_text = tk.Text(
            thanks_frame, 
            height=10, 
            width=50,
            wrap=tk.WORD,
            state='disabled'
        )
        thanks_text.pack(fill="both", expand=True, padx=10, pady=5)
        thanks_text.config(state='normal')
        thanks_text.insert(tk.END, "特别感谢：\n\n")
        thanks_text.insert(tk.END, "1. 所有测试用户 - 感谢你们的反馈和建议\n")
        thanks_text.insert(tk.END, "2. 开源社区 - 提供了很多优秀的工具和库\n")
        thanks_text.insert(tk.END, "3. Python 社区 - 提供了强大的编程语言和生态系统\n")
        thanks_text.insert(tk.END, "4. Tkinter 库 - 提供了跨平台的 GUI 开发支持\n")
        thanks_text.insert(tk.END, "5. Pillow 库 - 提供了强大的图像处理功能\n")
        thanks_text.insert(tk.END, "6. Message-Pop-UP - 提供了消息弹窗功能支持\n\n")
        thanks_text.insert(tk.END, "没有你们的支持，就没有 Filesa COChat 的今天！")
        thanks_text.config(state='disabled')
    
    def apply_font_settings(self, font_name, font_size, is_bold):
        """应用字体设置"""
        font_weight = "bold" if is_bold else "normal"
        new_font = (font_name, font_size, font_weight)
        
        # 更新聊天区域字体
        self.chat_area.config(font=new_font)
        self.chat_font = new_font
        
        # 更新输入框字体
        self.message_entry.config(font=(font_name, font_size))
        
        # 更新用户列表字体
        self.user_list_font = (font_name, font_size - 1)
        self.user_listbox.config(font=self.user_list_font)
        self.user_count_label.config(font=self.user_list_font)
        
        messagebox.showinfo("设置已应用", "字体设置已成功应用到聊天界面")
    
    def process_queue(self):
        """处理来自线程的消息队列"""
        try:
            while True:
                msg_type, data = self.message_queue.get_nowait()
                if msg_type == "search_result":
                    # 搜索完成，处理结果
                    self.handle_search_result(data)
        except queue.Empty:
            pass
        
        # 每隔100毫秒检查一次队列
        self.root.after(100, self.process_queue)
    
    def handle_search_result(self, existing_server):
        """处理搜索聊天室的结果"""
        # 隐藏进度条，显示聊天界面
        self.progress_frame.pack_forget()
        self.chat_frame.pack(fill=tk.BOTH, expand=True)
        
        self.add_message("系统", f"欢迎 {self.username} 使用 Filesa COChat")
        
        if existing_server:
            # 如果找到，加入聊天室
            if not self.join_chatroom(existing_server):
                # 如果加入失败，创建新聊天室
                if not self.create_chatroom():
                    messagebox.showerror("错误", "无法创建聊天室")
                    self.root.destroy()
                    return
        else:
            # 如果没找到，创建新聊天室
            self.add_message("系统", "未找到现有聊天室，创建新聊天室...")
            if not self.create_chatroom():
                messagebox.showerror("错误", "无法创建聊天室")
                self.root.destroy()
                return
    
    def login(self, event=None):
        """处理登录逻辑"""
        self.username = self.username_entry.get().strip()
        if not self.username:
            messagebox.showwarning("警告", "必须输入用户名")
            return
        
        # 隐藏登录界面，显示进度条
        self.login_frame.pack_forget()
        self.progress_frame.pack(fill=tk.BOTH, expand=True, padx=20, pady=20)
        
        # 启动队列处理
        self.root.after(100, self.process_queue)
        
        # 在后台线程中搜索聊天室
        search_thread = threading.Thread(target=self.search_chatroom_thread)
        search_thread.daemon = True
        search_thread.start()
    
    def start(self):
        """启动聊天软件"""
        # 启动GUI主循环
        self.root.protocol("WM_DELETE_WINDOW", self.on_closing)
        self.root.mainloop()
    
    def on_closing(self):
        """关闭应用程序时的清理工作"""
        # 如果是服务器，先迁移服务器
        if self.is_server and len(self.user_list) > 1:
            if self.initiate_migration():
                self.add_message("系统", "服务器已迁移，正在关闭...")
                time.sleep(1)  # 给迁移消息发送留出时间
            else:
                self.add_message("系统", "服务器迁移失败，直接关闭")
        
        self.running = False
        self.scan_complete.set()  # 确保扫描线程退出
        
        # 关闭所有客户端连接
        with self.client_sockets_lock:
            for client_socket in self.client_sockets:
                try:
                    client_socket.close()
                except:
                    pass
        
        # 关闭服务器socket
        if self.server_socket:
            try:
                self.server_socket.close()
            except:
                pass
        
        self.root.destroy()

if __name__ == "__main__":
    app = FilesaCOChat()
    app.start()
