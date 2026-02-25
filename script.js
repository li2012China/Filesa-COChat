// Filesa-COChat 网站交互脚本

document.addEventListener('DOMContentLoaded', function() {
    // 设置当前年份
    initCurrentYear();
    
    // 导航栏滚动效果
    initNavbarScroll();
    
    // 平滑滚动
    initSmoothScroll();
    
    // 聊天演示动画
    initChatDemo();
    
    // 功能卡片动画
    initFeatureCards();
    
    // 使用步骤动画
    initUsageSteps();
    
    // FAQ 展开/收起
    initFAQ();
    
    // 移动端菜单切换
    initMobileMenu();
});

// 导航栏滚动效果
function initNavbarScroll() {
    const navbar = document.querySelector('.navbar');
    let lastScroll = 0;
    
    window.addEventListener('scroll', function() {
        const currentScroll = window.pageYOffset;
        
        if (currentScroll > 100) {
            navbar.style.backgroundColor = 'rgba(23, 23, 23, 0.98)';
        } else {
            navbar.style.backgroundColor = 'rgba(23, 23, 23, 0.95)';
        }
        
        lastScroll = currentScroll;
    });
}

// 平滑滚动
function initSmoothScroll() {
    document.querySelectorAll('a[href^="#"]').forEach(anchor => {
        anchor.addEventListener('click', function(e) {
            e.preventDefault();
            const target = document.querySelector(this.getAttribute('href'));
            if (target) {
                target.scrollIntoView({
                    behavior: 'smooth',
                    block: 'start'
                });
            }
        });
    });
}

// 应用演示动画 - 模拟登录和聊天界面切换
function initChatDemo() {
    const loginView = document.getElementById('app-login');
    const chatView = document.getElementById('app-chat');
    const chatMessages = document.querySelector('.chat-messages-demo');
    const userList = document.querySelector('.user-list-demo');
    const userPanelHeader = document.querySelector('.user-panel-header');
    const loginUsername = document.getElementById('login-username');
    
    if (!loginView || !chatView) return;
    
    // 用户名逐字出现效果
    let typeInterval = null;
    
    function typeUsername() {
        const usernameInput = document.getElementById('login-username');
        if (!usernameInput) {
            return;
        }
        
        // 清除之前的定时器
        if (typeInterval) {
            clearInterval(typeInterval);
        }
        
        const targetUsername = 'li2012China';
        usernameInput.value = '';
        let charIndex = 0;
        
        typeInterval = setInterval(() => {
            if (charIndex < targetUsername.length) {
                usernameInput.value += targetUsername.charAt(charIndex);
                charIndex++;
            } else {
                clearInterval(typeInterval);
                typeInterval = null;
            }
        }, 150);
    }
    
    // 模拟消息数据
    const messages = [
        { type: 'system', text: '你已成功加入频道' },
        { user: 'xiaoyang2011', time: '10:30:25', text: '大家好！这个聊天工具真方便~', type: 'other' },
        { user: 'li2012China', time: '10:30:32', text: '是啊，不用配置服务器就能用！', type: 'self' },
        { type: 'system', text: 'The Empire 加入了频道', addUser: 'The Empire' },
        { user: 'Spooooke', time: '10:30:45', text: '自动发现功能太棒了 👍', type: 'other' },
        { user: 'wangkaikai111', time: '10:31:02', text: '上信息课交流更方便啦！', type: 'other' },
        { user: 'HANANIHA', time: '10:31:15', text: '支持开源！', type: 'other' }
    ];
    
    // 初始用户列表（不包含 The Empire）
    const initialUsers = ['li2012China', 'xiaoyang2011', 'Spooooke', 'wangkaikai111', 'HANANIHA'];
    
    let currentPhase = 'login'; // login -> chat -> reset
    let messageIndex = 0;
    let currentUserCount = initialUsers.length;
    
    function initUserList() {
        if (!userList) return;
        userList.innerHTML = '';
        initialUsers.forEach(user => {
            const userItem = document.createElement('div');
            userItem.className = 'user-item-demo';
            userItem.innerHTML = `<span class="user-dot"></span>${user}`;
            userList.appendChild(userItem);
        });
        if (userPanelHeader) {
            userPanelHeader.textContent = `在线用户 (${initialUsers.length})`;
        }
        currentUserCount = initialUsers.length;
    }
    
    function addUserToList(username) {
        if (!userList) return;
        const userItem = document.createElement('div');
        userItem.className = 'user-item-demo';
        userItem.style.animation = 'fadeIn 0.3s ease';
        userItem.innerHTML = `<span class="user-dot"></span>${username}`;
        userList.appendChild(userItem);
        currentUserCount++;
        if (userPanelHeader) {
            userPanelHeader.textContent = `在线用户 (${currentUserCount})`;
        }
    }
    
    function switchToChat() {
        // 淡出登录界面
        loginView.style.opacity = '0';
        loginView.style.transition = 'opacity 0.3s ease';
        
        setTimeout(() => {
            loginView.style.display = 'none';
            chatView.style.display = 'flex';
            chatView.style.opacity = '0';
            
            // 淡入聊天界面
            setTimeout(() => {
                chatView.style.transition = 'opacity 0.3s ease';
                chatView.style.opacity = '1';
                currentPhase = 'chat';
                messageIndex = 0;
                // 初始化用户列表
                initUserList();
                // 清空并重新填充消息
                if (chatMessages) {
                    chatMessages.innerHTML = '';
                    addNextMessage();
                }
            }, 50);
        }, 300);
    }
    
    function switchToLogin() {
        // 淡出聊天界面
        chatView.style.opacity = '0';
        
        setTimeout(() => {
            chatView.style.display = 'none';
            loginView.style.display = 'flex';
            loginView.style.opacity = '0';
            
            // 清空用户名输入框
            const usernameInput = document.getElementById('login-username');
            if (usernameInput) {
                usernameInput.value = '';
            }
            
            // 淡入登录界面
            setTimeout(() => {
                loginView.style.transition = 'opacity 0.3s ease';
                loginView.style.opacity = '1';
                currentPhase = 'login';
                // 重新开始循环
                setTimeout(startDemo, 2000);
            }, 50);
        }, 300);
    }
    
    function getCurrentTime() {
        const now = new Date();
        return now.toLocaleTimeString('zh-CN', { hour12: false });
    }
    
    function addNextMessage() {
        if (messageIndex >= messages.length) {
            // 消息展示完毕，等待后重置
            setTimeout(switchToLogin, 3000);
            return;
        }
        
        const msg = messages[messageIndex];
        const msgDiv = document.createElement('div');
        const currentTime = getCurrentTime();
        
        if (msg.type === 'system') {
            msgDiv.className = 'msg-system';
            msgDiv.textContent = `${msg.text}`;
            // 如果系统消息需要添加用户到列表
            if (msg.addUser) {
                setTimeout(() => {
                    addUserToList(msg.addUser);
                }, 300);
            }
        } else {
            msgDiv.className = msg.type === 'self' ? 'msg-self' : 'msg-other';
            msgDiv.innerHTML = `
                <div class="msg-header">${msg.user} <span class="msg-time">${currentTime}</span></div>
                <div class="msg-content">${msg.text}</div>
            `;
        }
        
        msgDiv.style.animation = 'fadeIn 0.3s ease';
        chatMessages.appendChild(msgDiv);
        chatMessages.scrollTop = chatMessages.scrollHeight;
        
        messageIndex++;
        setTimeout(addNextMessage, 1500);
    }
    
    function startDemo() {
        // 先清空输入框确保干净状态
        const usernameInput = document.getElementById('login-username');
        if (usernameInput) {
            usernameInput.value = '';
        }
        // 启动用户名逐字出现效果
        typeUsername();
        // 开始登录界面展示
        setTimeout(switchToChat, 3500);
    }
    
    // 启动演示
    startDemo();
    
    // 用户发送消息功能
    initUserChat();
    
    function initUserChat() {
        const chatInput = document.getElementById('chat-input');
        const sendBtn = document.getElementById('chat-send-btn');
        
        if (!chatInput || !sendBtn || !chatMessages) return;
        
        function sendMessage() {
            const text = chatInput.value.trim();
            if (!text) return;
            
            // 创建用户消息
            const msgDiv = document.createElement('div');
            const currentTime = getCurrentTime();
            msgDiv.className = 'msg-self';
            msgDiv.innerHTML = `
                <div class="msg-header">li2012China <span class="msg-time">${currentTime}</span></div>
                <div class="msg-content">${text}</div>
            `;
            msgDiv.style.animation = 'fadeIn 0.3s ease';
            chatMessages.appendChild(msgDiv);
            chatMessages.scrollTop = chatMessages.scrollHeight;
            
            // 清空输入框
            chatInput.value = '';
        }
        
        // 点击发送按钮
        sendBtn.addEventListener('click', sendMessage);
        
        // 按回车键发送
        chatInput.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') {
                sendMessage();
            }
        });
    }
}

// 功能卡片动画
function initFeatureCards() {
    const cards = document.querySelectorAll('.feature-card');
    
    const observer = new IntersectionObserver((entries) => {
        entries.forEach((entry, index) => {
            if (entry.isIntersecting) {
                setTimeout(() => {
                    entry.target.style.opacity = '1';
                    entry.target.style.transform = 'translateY(0)';
                }, index * 100);
            }
        });
    }, { threshold: 0.1 });
    
    cards.forEach(card => {
        card.style.opacity = '0';
        card.style.transform = 'translateY(20px)';
        card.style.transition = 'all 0.5s ease';
        observer.observe(card);
    });
}

// 使用步骤动画
function initUsageSteps() {
    const steps = document.querySelectorAll('.usage-step');
    
    const observer = new IntersectionObserver((entries) => {
        entries.forEach((entry, index) => {
            if (entry.isIntersecting) {
                setTimeout(() => {
                    entry.target.style.opacity = '1';
                    entry.target.style.transform = 'translateX(0)';
                }, index * 200);
            }
        });
    }, { threshold: 0.2 });
    
    steps.forEach((step, index) => {
        step.style.opacity = '0';
        step.style.transform = 'translateX(-30px)';
        step.style.transition = 'all 0.6s ease';
        observer.observe(step);
    });
}

// FAQ 展开/收起
function initFAQ() {
    const faqItems = document.querySelectorAll('.faq-item');
    
    faqItems.forEach(item => {
        const question = item.querySelector('.faq-question');
        const answer = item.querySelector('.faq-answer');
        
        question.style.cursor = 'pointer';
        answer.style.maxHeight = '0px';
        answer.style.overflow = 'hidden';
        answer.style.transition = 'max-height 0.3s ease';
        
        question.addEventListener('click', () => {
            const isCollapsed = answer.style.maxHeight === '0px';
            answer.style.maxHeight = isCollapsed ? answer.scrollHeight + 'px' : '0px';
        });
    });
}

// 移动端菜单切换
function initMobileMenu() {
    const menuToggle = document.getElementById('mobile-menu-toggle');
    const navLinks = document.getElementById('nav-links');
    
    if (!menuToggle || !navLinks) return;
    
    menuToggle.addEventListener('click', () => {
        navLinks.classList.toggle('active');
        const icon = menuToggle.querySelector('i');
        if (icon) {
            icon.classList.toggle('fa-bars');
            icon.classList.toggle('fa-times');
        }
    });
    
    // 点击导航链接后关闭菜单
    navLinks.querySelectorAll('.nav-link').forEach(link => {
        link.addEventListener('click', () => {
            navLinks.classList.remove('active');
            const icon = menuToggle.querySelector('i');
            if (icon) {
                icon.classList.add('fa-bars');
                icon.classList.remove('fa-times');
            }
        });
    });
}

// 设置当前年份
function initCurrentYear() {
    const yearElement = document.getElementById('current-year');
    if (yearElement) {
        yearElement.textContent = new Date().getFullYear();
    }
}

// 添加打字机效果到标题
function typeWriterEffect(element, text, speed = 100) {
    let i = 0;
    element.textContent = '';
    
    function type() {
        if (i < text.length) {
            element.textContent += text.charAt(i);
            i++;
            setTimeout(type, speed);
        }
    }
    
    type();
}

// 页面加载完成后的特效
document.addEventListener('DOMContentLoaded', function() {
    // 为按钮添加点击波纹效果
    document.querySelectorAll('.btn').forEach(button => {
        button.addEventListener('click', function(e) {
            const ripple = document.createElement('span');
            const rect = this.getBoundingClientRect();
            const size = Math.max(rect.width, rect.height);
            const x = e.clientX - rect.left - size / 2;
            const y = e.clientY - rect.top - size / 2;
            
            ripple.style.cssText = `
                position: absolute;
                width: ${size}px;
                height: ${size}px;
                left: ${x}px;
                top: ${y}px;
                background: rgba(255, 255, 255, 0.3);
                transform: scale(0);
                animation: ripple 0.6s ease-out;
                pointer-events: none;
            `;
            
            this.style.position = 'relative';
            this.style.overflow = 'hidden';
            this.appendChild(ripple);
            
            setTimeout(() => ripple.remove(), 600);
        });
    });
});


