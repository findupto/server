import 'package:flutter/material.dart';
import 'core/api_client.dart';

void main() => runApp(const PosMobileApp());

class PosMobileApp extends StatelessWidget {
  const PosMobileApp({super.key});
  @override
  Widget build(BuildContext context) => MaterialApp(title: 'MK Pizza & Ice Bar', theme: ThemeData(useMaterial3: true), home: const CustomerHome());
}

class CustomerHome extends StatefulWidget {
  const CustomerHome({super.key});
  @override
  State<CustomerHome> createState() => _CustomerHomeState();
}

class _CustomerHomeState extends State<CustomerHome> {
  final api = PosApiClient(baseUrl: const String.fromEnvironment('POS_SERVER_URL', defaultValue: 'http://10.0.2.2:5000'));
  final cart = <int, Map<String, dynamic>>{};
  List<dynamic> products = [];
  bool loading = true;
  String? error;

  @override
  void initState() { super.initState(); _load(); }

  Future<void> _load() async {
    try {
      final existing = await api.token();
      if (existing == null) await api.createCustomerSession(name: 'Guest');
      products = await api.products();
    } catch (e) { error = e.toString(); }
    if (mounted) setState(() => loading = false);
  }

  void _add(Map<String, dynamic> product) {
    final id = product['id'] as int;
    setState(() {
      final current = cart[id];
      cart[id] = {'productId': id, 'name': product['name'], 'price': product['price'], 'quantity': (current?['quantity'] ?? 0) + 1};
    });
  }

  Future<void> _checkout() async {
    if (cart.isEmpty) return;
    try {
      await api.createOrder(items: cart.values.map((x) => {'productId': x['productId'], 'quantity': x['quantity']}).toList());
      if (mounted) { setState(cart.clear); ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Order placed successfully'))); }
    } catch (e) { if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.toString()))); }
  }

  @override
  Widget build(BuildContext context) {
    final total = cart.values.fold<double>(0, (sum, x) => sum + (x['price'] as num).toDouble() * (x['quantity'] as int));
    return Scaffold(
      appBar: AppBar(title: const Text('MK Pizza & Ice Bar'), actions: [IconButton(onPressed: _checkout, icon: Badge(label: Text('${cart.values.fold<int>(0, (s, x) => s + x['quantity'] as int)}'), child: const Icon(Icons.shopping_cart))) ]),
      body: loading ? const Center(child: CircularProgressIndicator()) : error != null ? Center(child: Text(error!)) : RefreshIndicator(onRefresh: _load, child: ListView.builder(itemCount: products.length, itemBuilder: (_, i) { final p = Map<String, dynamic>.from(products[i]); return ListTile(title: Text(p['name']), subtitle: Text(p['description'] ?? ''), trailing: Text('Rs. ${p['price']}'), onTap: () => _add(p)); })),
      bottomNavigationBar: cart.isEmpty ? null : SafeArea(child: Padding(padding: const EdgeInsets.all(12), child: FilledButton.icon(onPressed: _checkout, icon: const Icon(Icons.receipt_long), label: Text('Place order • Rs. ${total.toStringAsFixed(2)}')))),
    );
  }
}
