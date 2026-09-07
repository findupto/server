import 'package:flutter/material.dart';
import 'core/api_client.dart';
import 'features/customer/parcel_tracking_page.dart';

void main() => runApp(const PosMobileApp());

class PosMobileApp extends StatelessWidget {
  const PosMobileApp({super.key});

  @override
  Widget build(BuildContext context) => MaterialApp(
        title: 'FindUpTo POS',
        theme: ThemeData(useMaterial3: true),
        home: const CustomerHome(),
      );
}

class CustomerHome extends StatefulWidget {
  const CustomerHome({super.key});

  @override
  State<CustomerHome> createState() => _CustomerHomeState();
}

class _CustomerHomeState extends State<CustomerHome> {
  final api = PosApiClient(
    baseUrl: const String.fromEnvironment(
      'POS_SERVER_URL',
      defaultValue: 'http://127.0.0.1:5000',
    ),
  );
  final cart = <int, Map<String, dynamic>>{};
  List<dynamic> products = [];
  String businessName = 'FindUpTo POS';
  bool loading = true;
  bool delivery = false;
  String? error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final existing = await api.token();
      if (existing == null) await api.createCustomerSession(name: 'Guest');
      final settings = await api.settings();
      final configuredName = '${settings['businessName'] ?? ''}'.trim();
      products = await api.products();
      if (configuredName.isNotEmpty) businessName = configuredName;
      error = null;
    } catch (e) {
      error = e.toString();
    }
    if (mounted) setState(() => loading = false);
  }

  void _add(Map<String, dynamic> product) {
    final id = (product['id'] as num).toInt();
    setState(() {
      final current = cart[id];
      cart[id] = {
        'productId': id,
        'name': product['name'],
        'price': product['price'],
        'quantity': (current?['quantity'] ?? 0) + 1,
      };
    });
  }

  Future<void> _checkout() async {
    if (cart.isEmpty) return;
    try {
      final result = await api.createOrder(
        items: cart.values
            .map((x) => {
                  'productId': x['productId'],
                  'quantity': x['quantity'],
                })
            .toList(),
        orderType: delivery ? 'Delivery' : 'Pickup',
      );
      if (!mounted) return;
      setState(cart.clear);
      final orderId = (result['id'] ?? result['orderId']) as num?;
      if (delivery && orderId != null) {
        final tracking = await api.customerOrderTracking(orderId.toInt());
        if (!mounted) return;
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Delivery tracking: ${tracking['trackingCode']}')),
        );
        Navigator.push(
          context,
          MaterialPageRoute(
            builder: (_) => ParcelTrackingPage(
              api: api,
              initialTrackingCode: '${tracking['trackingCode']}',
            ),
          ),
        );
      } else {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Order placed successfully')),
        );
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(e.toString())),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final total = cart.values.fold<double>(
      0,
      (sum, x) => sum +
          (x['price'] as num).toDouble() * (x['quantity'] as int),
    );
    final itemCount = cart.values.fold<int>(
      0,
      (sum, x) => sum + (x['quantity'] as int),
    );

    return Scaffold(
      appBar: AppBar(
        title: Text(businessName),
        actions: [
          IconButton(
            onPressed: () => Navigator.push(
              context,
              MaterialPageRoute(
                builder: (_) => ParcelTrackingPage(api: api),
              ),
            ),
            icon: const Icon(Icons.location_searching),
          ),
          IconButton(
            onPressed: _checkout,
            icon: Badge(
              label: Text('$itemCount'),
              child: const Icon(Icons.shopping_cart),
            ),
          ),
        ],
      ),
      body: loading
          ? const Center(child: CircularProgressIndicator())
          : error != null
              ? Center(
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(error!, textAlign: TextAlign.center),
                      const SizedBox(height: 12),
                      FilledButton.icon(
                        onPressed: () {
                          setState(() => loading = true);
                          _load();
                        },
                        icon: const Icon(Icons.refresh),
                        label: const Text('Retry'),
                      ),
                    ],
                  ),
                )
              : Column(
                  children: [
                    SwitchListTile(
                      title: const Text('Delivery order'),
                      subtitle: Text(
                        delivery
                            ? 'Rider tracking will be available'
                            : 'Pickup from store',
                      ),
                      value: delivery,
                      onChanged: (v) => setState(() => delivery = v),
                    ),
                    Expanded(
                      child: RefreshIndicator(
                        onRefresh: _load,
                        child: ListView.builder(
                          itemCount: products.length,
                          itemBuilder: (_, i) {
                            final product =
                                Map<String, dynamic>.from(products[i]);
                            return ListTile(
                              title: Text('${product['name']}'),
                              subtitle: Text('${product['description'] ?? ''}'),
                              trailing: Text('Rs. ${product['price']}'),
                              onTap: () => _add(product),
                            );
                          },
                        ),
                      ),
                    ),
                  ],
                ),
      bottomNavigationBar: cart.isEmpty
          ? null
          : SafeArea(
              child: Padding(
                padding: const EdgeInsets.all(12),
                child: FilledButton.icon(
                  onPressed: _checkout,
                  icon: const Icon(Icons.receipt_long),
                  label: Text(
                    'Place ${delivery ? 'delivery' : 'pickup'} • Rs. ${total.toStringAsFixed(2)}',
                  ),
                ),
              ),
            ),
    );
  }
}
