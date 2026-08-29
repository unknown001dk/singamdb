from setuptools import setup, find_packages

setup(
    name="singamdb",
    version="3.0.0",
    description="Official Python Client Driver for SingamDB Database (TCP Wire Protocol)",
    long_description="High-performance binary TCP client driver for SingamDB storage engine and database server.",
    author="unknown001dk",
    url="https://github.com/unknown001dk/singamdb",
    packages=find_packages(),
    py_modules=["singamdb"],
    python_requires=">=3.8",
    classifiers=[
        "Programming Language :: Python :: 3",
        "License :: OSI Approved :: MIT License",
        "Operating System :: OS Independent",
        "Topic :: Database :: Front-Ends",
    ],
)
