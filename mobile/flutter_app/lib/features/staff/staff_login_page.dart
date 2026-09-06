import 'package:flutter/material.dart';
import '../../core/api_client.dart';
import 'staff_dashboard_page.dart';

class StaffLoginPage extends StatefulWidget {
  const StaffLoginPage({super.key, required this.api});
  final PosApiClient api;
  @override State<StaffLoginPage> createState() => _StaffLoginPageState();
}

class _StaffLoginPageState extends State<StaffLoginPage> {
  final username = TextEditingController();
  final password = TextEditingController();
  bool busy = false;
  String? error;

  Future<void> login() async {
    if (username.text.trim().isEmpty || password.text.isEmpty) return;
    setState(() { busy = true; error = null; });
    try {
      final result = await widget.api.login(username.text.trim(), password.text);
      if (!mounted) return;
      Navigator.pushReplacement(context, MaterialPageRoute(builder: (_) => StaffDashboardPage(api: widget.api, role: result['role'].toString())));
    } catch (e) {
      if (mounted) setState(() => error = 'Login failed. Check username and password.');
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  void dispose() { username.dispose(); password.dispose(); super.dispose(); }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Staff Login')),
    body: Center(child: ConstrainedBox(constraints: const BoxConstraints(maxWidth: 420), child: Padding(
      padding: const EdgeInsets.all(24),
      child: Column(mainAxisSize: MainAxisSize.min, children: [
        TextField(controller: username, textInputAction: TextInputAction.next, decoration: const InputDecoration(labelText: 'Username')),
        const SizedBox(height: 12),
        TextField(controller: password, obscureText: true, onSubmitted: (_) => login(), decoration: const InputDecoration(labelText: 'Password')),
        if (error != null) ...[const SizedBox(height: 12), Text(error!, style: TextStyle(color: Colors.red))],
        const SizedBox(height: 20),
        SizedBox(width: double.infinity, child: FilledButton(onPressed: busy ? null : login, child: busy ? const SizedBox(width: 20,height:20,child:CircularProgressIndicator(strokeWidth:2)) : const Text('Sign in'))),
      ]),
    )))
  );
}
